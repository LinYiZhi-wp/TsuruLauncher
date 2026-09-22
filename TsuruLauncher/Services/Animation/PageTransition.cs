using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace TsuruLauncher.Services.Animation
{
    public enum TransitionType
    {
        SlideIn,
        SlideOut,
        FadeSlideIn,
        FadeSlideOut,
        ScaleIn,
        ScaleOut,
        FadeIn,
        FadeOut,
        SlideUp,
        SlideDown,
        None
    }

    /// <summary>
    /// 全局过渡动画入口。所有时长 / 曲线 / 位移 / 缩放都来自
    /// <see cref="AxolotlMotion"/>（即 Axolotl 源码里的原始数值），本文件只负责把它们
    /// 落到 WPF 的 Storyboard 上。
    ///
    /// 性能约定：只动 Opacity 与 RenderTransform（TranslateTransform / ScaleTransform），
    /// 绝不把 Width / Height / Margin 当动画属性；右栏这种必须改宽度的场景用
    /// <see cref="GridLengthAnimation"/> 直接动画 Grid 列宽（Axolotl 也是这样动画
    /// grid-template-columns 上的 --right-bar-width），并给容器挂 BitmapCache。
    /// </summary>
    public static class PageTransition
    {
        private static readonly Dictionary<FrameworkElement, Storyboard> _activeStoryboards = new();

        /// <summary>
        /// 这一轮动效正在动的元素（页面根 / 内容容器 / 错峰卡片）。
        /// 新导航到来时先 <see cref="SettleActiveMotion"/> 把它们全部立即归位，
        /// 否则被中断的动画会停在起始值上（translateY=+30 / -6px），整页就"卡"在那个偏移。
        /// </summary>
        private static readonly List<FrameworkElement> _motionTargets = new List<FrameworkElement>();

        /// <summary>兜底定时器：Completed 因为任何原因没来时，也必须把元素落到终态、清掉时钟。</summary>
        private static readonly Dictionary<FrameworkElement, System.Windows.Threading.DispatcherTimer> _finalizeTimers = new();

        /// <summary>
        /// 每个元素「最近一次播放」的轮次号。终态只允许由**当前这一轮**落地：
        /// <c>Storyboard.Completed</c> 与 <see cref="SettleAfter"/> 兜底定时器谁先到谁落地，
        /// 输的那个、以及被 <see cref="StopActive"/> 停掉的上一轮，一律作废。
        ///
        /// 为什么必须有：点击「进入 / 返回」这类面板显隐时，同一个元素会被反复播放
        /// （连点、动画没跑完就反向）。如果旧一轮的收尾回调还能执行，它会把新一轮
        /// **刚设好的终态覆盖回旧终态**（例如离场回调把已经是 Visible 的覆盖层又设成
        /// Collapsed，或者进场回调把正在淡出的面板又拉回 Opacity=1）——
        /// 这正是"动画被自身 settle 逻辑反复重置""覆盖层停在中间态"的来源。
        /// </summary>
        private static readonly Dictionary<FrameworkElement, int> _playTokens = new();

        /// <summary>开启新一轮播放：返回本轮轮次号，之前所有轮次的收尾自动作废。</summary>
        private static int NextPlayToken(FrameworkElement target)
        {
            int token = _playTokens.TryGetValue(target, out var current) ? current + 1 : 1;
            _playTokens[target] = token;
            return token;
        }

        /// <summary>该轮次是否仍是这个元素当前的有效播放。</summary>
        private static bool IsCurrentPlay(FrameworkElement target, int token)
            => _playTokens.TryGetValue(target, out var current) && current == token;

        /// <summary>系统「减少动画」开关；默认（开关关闭）= 完整播放 Axolotl 原版动效。</summary>
        public static bool AnimationsEnabled => AxolotlMotion.FullMotionEnabled;

        /// <summary>
        /// 故障注入开关（默认关，只给动效健壮性测试用）：
        /// <c>TSURU_MOTION_FAULT=freeze</c> 时 <see cref="PlayPopup"/> 这类 Storyboard 动画
        /// **故意不启动**，并把元素冻结在中间的半透明值上 —— 精确复现
        /// "动画时钟没有被推进 / Completed 永远不来，覆盖层停在半透明"这一类现场。
        /// </summary>
        public static bool FaultFreezePopup
        {
            get
            {
                try
                {
                    return string.Equals(Environment.GetEnvironmentVariable("TSURU_MOTION_FAULT"),
                        "freeze", StringComparison.OrdinalIgnoreCase);
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// <c>TSURU_MOTION_WATCHDOG=0</c>：关掉兜底（<c>=lab</c> 只关
        /// <see cref="PageTransition"/> 这一层、保留页内那层），等价于**改前**的行为。
        /// 留着它做 A/B 对照：同一份构建里跑两次，就能看到
        /// "没有兜底 = 覆盖层永远停在半透明、点什么都没反应" 和
        /// "有兜底 = 260ms 内落终态、页面照常可点" 的区别。
        /// </summary>
        public static bool FinalizeWatchdogEnabled => WatchdogMode != "0";

        /// <summary>
        /// 只关掉 <see cref="PageTransition"/> 这一层的兜底（页内自己的兜底仍然生效），
        /// 用来单独验证"第二层防线"：<c>TSURU_MOTION_WATCHDOG=lab</c>。
        /// </summary>
        public static bool TransitionWatchdogEnabled => WatchdogMode != "0" && WatchdogMode != "lab";

        private static string WatchdogMode
        {
            get
            {
                try { return (Environment.GetEnvironmentVariable("TSURU_MOTION_WATCHDOG") ?? string.Empty).Trim(); }
                catch { return string.Empty; }
            }
        }

        /// <summary>
        /// 启动预热：把动效的代码路径（EnsureScale/EnsureTranslate、PlayMotion、SettleAfter、
        /// FinalizeElement 这一串）在一棵**离屏**小元素上先跑一遍再立刻归位。
        ///
        /// 为什么需要：这些方法的 JIT + 首次 Freezable/Storyboard 分配都发生在第一次导航里，
        /// 实测让首次 <c>page-switch</c> 的同步阻塞落在 3.1~3.6ms（越过 3ms 预算），
        /// 而之后每一次点击都只有 0.11~1.3ms —— 那是纯冷启动成本，不该算在"点击"头上。
        /// 预热后首次导航也回到预算内。
        /// </summary>
        public static void WarmUp()
        {
            try
            {
                var probe = new System.Windows.Controls.Grid { Width = 8, Height = 8 };
                PlayPageEnter(probe, true);
                SettleElement(probe);
                PlayHostOverlapFade(probe);
                SettleElement(probe);
            }
            catch { }
        }

        #region Content-switch prewarm（切换前预热目标元素，任务 q1）

        /// <summary>
        /// 内容切换（<see cref="PlayContentSwitch"/>）自动预热开关，默认**开**。
        /// <c>TSURU_MOTION_PREWARM=0</c>（或 off/false）关掉它 —— 同一份构建跑两次就能做
        /// 改前 / 改后 A/B，不需要重新编译。
        /// </summary>
        public static bool ContentSwitchPrewarmEnabled
        {
            get
            {
                string mode = PrewarmMode;
                return mode != "0" && mode != "off" && mode != "false";
            }
        }

        /// <summary>预热提前量（ms）。默认 100ms；<c>TSURU_MOTION_PREWARM_MS</c> 可覆盖。</summary>
        public static double ContentSwitchPrewarmLeadMs { get; } = ReadPrewarmLeadMs();

        private static string PrewarmMode
        {
            get
            {
                try { return (Environment.GetEnvironmentVariable("TSURU_MOTION_PREWARM") ?? string.Empty).Trim().ToLowerInvariant(); }
                catch { return string.Empty; }
            }
        }

        private static double ReadPrewarmLeadMs()
        {
            try
            {
                string raw = (Environment.GetEnvironmentVariable("TSURU_MOTION_PREWARM_MS") ?? string.Empty).Trim();
                if (double.TryParse(raw, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double ms) && ms >= 0.0 && ms <= 2000.0)
                    return ms;
            }
            catch { }
            return 100.0;
        }

        /// <summary>
        /// 内容切换动画开始前 <paramref name="leadMs"/>（默认 100ms）自动预热目标元素。
        ///
        /// 用 Background 优先级的一次性 <see cref="System.Windows.Threading.DispatcherTimer"/> 投递：
        ///   * 立刻返回，绝不占用点击回调的同步时间（预算 3ms）；
        ///   * Background 低于 Render / Input，所以预热不会和正在播放的动画抢渲染帧。
        /// 真正的 measure/arrange 预热在 <see cref="MotionAssist.Prewarm"/> 里一个 Dispatcher 回调内完成
        /// （临时 Visible -&gt; UpdateLayout -&gt; 还原，不闪帧）。
        ///
        /// 调用方如果能在更早的时机（hover / PreviewMouseDown）就知道目标元素，直接调它比等
        /// 100ms 提前量更划算 —— 这也是把本方法公开出去的原因。
        /// </summary>
        public static void PrewarmTarget(FrameworkElement? element, double leadMs = 100.0)
        {
            if (element == null) return;
            var dispatcher = element.Dispatcher;
            if (dispatcher == null) return;

            double lead = Math.Max(0.0, leadMs);
            System.Windows.Threading.DispatcherTimer? timer = null;
            timer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(lead),
                System.Windows.Threading.DispatcherPriority.Background,
                (_, __) =>
                {
                    if (timer != null) { try { timer.Stop(); } catch { } }
                    MotionAssist.Prewarm(element);
                },
                dispatcher);
            timer.Start();

            try
            {
                TsuruLauncher.Utilities.Logger.LogInfo(
                    $"[MotionPerf] prewarm-scheduled {DescribeTarget(element)} lead={lead:0}ms " +
                    "priority=Background via=content-switch");
            }
            catch { }
        }

        /// <summary>日志用的元素短名（与 MotionAssist.Describe 同形）。</summary>
        private static string DescribeTarget(FrameworkElement fe)
            => string.IsNullOrEmpty(fe.Name) ? fe.GetType().Name : fe.GetType().Name + "#" + fe.Name;

        #endregion


        #region Generic

        /// <summary>
        /// 通用过渡。走 Axolotl 的 <c>.slide</c>（global.scss:302-317）：
        /// transform 0.2s ease + opacity 0.2s ease，位移 30px。
        /// </summary>
        public static void Play(FrameworkElement target, TransitionType type, Action? onCompleted = null)
        {
            if (target == null) { onCompleted?.Invoke(); return; }
            StopActive(target);

            if (type == TransitionType.None) { onCompleted?.Invoke(); return; }

            bool animateOpacity;
            double opacityFrom, opacityTo;
            double xFrom = 0, xTo = 0, yFrom = 0, yTo = 0;
            const double D = AxolotlMotion.SlideOffsetPx;

            switch (type)
            {
                case TransitionType.SlideIn:
                    animateOpacity = false; opacityFrom = 1; opacityTo = 1;
                    yFrom = D;
                    break;
                case TransitionType.SlideOut:
                    animateOpacity = false; opacityFrom = 1; opacityTo = 1;
                    yTo = -D;
                    break;
                case TransitionType.FadeSlideIn:
                    animateOpacity = true; opacityFrom = 0; opacityTo = 1;
                    yFrom = D;
                    break;
                case TransitionType.FadeSlideOut:
                    animateOpacity = true; opacityFrom = 1; opacityTo = 0;
                    yTo = -D;
                    break;
                case TransitionType.ScaleIn:
                    animateOpacity = true; opacityFrom = 0; opacityTo = 1;
                    break;
                case TransitionType.ScaleOut:
                    animateOpacity = true; opacityFrom = 1; opacityTo = 0;
                    break;
                case TransitionType.FadeIn:
                    animateOpacity = true; opacityFrom = 0; opacityTo = 1;
                    break;
                case TransitionType.FadeOut:
                    animateOpacity = true; opacityFrom = 1; opacityTo = 0;
                    break;
                case TransitionType.SlideUp:
                    animateOpacity = true; opacityFrom = 0; opacityTo = 1;
                    yFrom = D;
                    break;
                case TransitionType.SlideDown:
                    animateOpacity = true; opacityFrom = 1; opacityTo = 0;
                    yTo = -D;
                    break;
                default:
                    animateOpacity = true; opacityFrom = 0; opacityTo = 1;
                    break;
            }

            if (!AnimationsEnabled)
            {
                ApplyRestingState(target, animateOpacity ? opacityTo : (double?)null, xTo, yTo);
                onCompleted?.Invoke();
                return;
            }

            PlayMotion(target, animateOpacity, opacityFrom, opacityTo,
                xFrom, xTo, yFrom, yTo,
                AxolotlMotion.Ms(AxolotlMotion.SlideMs), AxolotlMotion.Ease, onCompleted);
        }

        #endregion

        #region Page transitions（Axolotl: grid-area 1/1 交叠，进入 180ms / 离开 120ms）

        /// <summary>
        /// 页面进场 —— <b>「一眼可见」档</b>（任务 §1）。
        ///
        /// 改前：<c>opacity 0-&gt;1 / 180ms ease</c> + <c>translateY ±30px-&gt;0 / 200ms ease</c>，
        /// 没有缩放 —— 实测（motion2 基线报告）虽然判定 ANIMATED，但幅度太小，
        /// 主观上就是「没有过渡动画」。
        ///
        /// 改后（原本三项同帧启动；r3 起整页根不再参与 scale，见下方说明）：
        ///   * opacity  0 -&gt; 1           220ms  ease（CSS cubic-bezier(0.25,0.1,0.25,1)）
        ///   * translateY ±56px -&gt; 0    280ms  cubic-bezier(0.22, 1, 0.36, 1)
        ///   * scale     0.985 -&gt; 1      280ms  同一条曲线
        /// 总时长 = max(220, 280, 280) = 280ms。
        /// <paramref name="isForward"/> = true（前进）时从下方 +56 推上来；后退时从上方 -56 落下来 ——
        /// 两个方向的符号严格互为镜像（<c>_isNavigatingBack</c> 决定）。
        ///
        /// <para><b>【r3 根因修复】整页根不再参与 scale。</b>
        /// 页面根实测 1,281,280 设备像素（1180x760 客户区、150% DPI）：在它上面逐帧 scale，
        /// 渲染线程每一帧都要重新栅格化整棵 1.28MP 子树（这正是"切页/切换那一帧掉帧"的成本）；
        /// 而给它挂 BitmapCache 又会把渲染线程打崩（帧间隔 1.2s / UCEERR_RENDERTHREADFAILURE，
        /// 见 <see cref="MotionAssist.MaxCacheDevicePixels"/> 的实测记录）。所以：
        ///   * translateY / opacity 仍然作用在页根（成本与纯淡入同级）；
        ///   * scale 只在**内层内容容器**（面积 ≤ 1.2MP）上做，并给同一个容器挂动效缓存 ——
        ///     内容静止、只有它自己的 RenderTransform 在变，缓存位图不会被逐帧作废；
        ///   * 内层容器也降不到 1.2MP 以下时，这一处**不做 scale、也不挂缓存**，只留 opacity + translateY。
        /// 埋点：<c>[MotionPerf] scale-host …</c> / <c>[MotionPerf] cache-target …</c>。</para>
        /// </summary>
        public static void PlayPageEnter(FrameworkElement page, bool isForward = true)
        {
            // 帧间隔采样（TSURU_FRAME_DBG=1 才开）：页面进场是本项目「公认流畅」的路径
            // （有缓存 + 下沉到内层容器），拿它当基准线，才能判断别处的帧率是不是真的差。
            MotionPerf.ProbeFrames("page-enter " + page.GetType().Name, 600);
            if (page == null) return;

            // ① 新导航到来：先把上一轮所有还在动的元素立即归位（这里不能碰新页自己，
            //    新页的错峰卡片也在动效集合里，所以按子树排除）。
            //    注意：宿主 PageTransitionHost 的回落补偿必须在**本次调用之后**启动，
            //    否则会被这一句立即归位掉（见 MainWindow.RootFrame_Navigated 的注释）。
            SettleActiveMotion(exceptSubtree: page);
            SettleElement(page);

            // 前进 / 后退的符号严格互为镜像。
            double yFrom = isForward ? AxolotlMotion.PageEnterSlidePx : -AxolotlMotion.PageEnterSlidePx;

            if (!AnimationsEnabled)
            {
                ApplyRestingState(page, 1, 0, 0);
                return;
            }

            // ── r3：页根本身绝不 scale（实测 1,281,280 设备像素）──────────────────────
            // 而且**这一刻布局还没跑**（实测 ActualWidth/Height = 0、候选清单为空，
            // 因为 Frame.Navigated / Loaded 都发生在 Measure 之前），所以：
            //   * 这一处不做 scale —— 任务要求就是"页面切换去掉 scale，只保留 opacity + translateY"；
            //   * 缓存留到**下一帧布局完成后**再下沉到内层内容容器挂上（AttachMotionCacheAfterLayout），
            //     挂之前先把这一帧画完，不占用点击回调、也不影响动画起点。
            FrameworkElement? scaleHost = null;
            ScaleTransform? scale = null;

            var translate = MotionVisuals.EnsureTranslate(page);
            page.BeginAnimation(UIElement.OpacityProperty, null);
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            page.Opacity = 0;
            translate.X = 0;
            translate.Y = yFrom;
            if (scale != null)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = scale.ScaleY = AxolotlMotion.PageEnterScaleFrom;
                RememberScaleHost(page, scaleHost);
            }
            RegisterTarget(page);

            // 动画期间给"真正在动的那个内层容器"挂缓存（挂不上就明确记一笔，绝不给整页根挂）。
            // 动画已经落终态（IsAnimating=false）就跳过 —— 不留下永久缓存。
            MotionAssist.AttachMotionCacheAfterLayout(page, "page-enter", () => IsAnimating(page));

            PlayMotion(page, true, 0, 1, 0, 0, yFrom, 0,
                AxolotlMotion.Ms(AxolotlMotion.PageEnterFadeMs), AxolotlMotion.Ease, null,
                beginTime: null,
                slideDuration: AxolotlMotion.Ms(AxolotlMotion.PageEnterSlideMs),
                slideEase: AxolotlMotion.PageEnterMoveEase,
                scaleFrom: scale != null ? AxolotlMotion.PageEnterScaleFrom : (double?)null,
                scaleTo: scale != null ? 1.0 : (double?)null,
                scaleDuration: AxolotlMotion.Ms(AxolotlMotion.PageEnterScaleMs),
                scaleEase: AxolotlMotion.PageEnterMoveEase,
                scaleHost: scaleHost);

            // ── 证据埋点：把「动画的**实际属性值**」按时采样写进日志 ──────────────
            // 只测像素很难区分「动画没跑」和「跑了但渲染没跟上」，所以这里直接读
            // 动画中的 Opacity / TranslateTransform.Y / ScaleTransform.ScaleX，
            // 以及页面在其可视父级里的**实际渲染位置**（TransformToAncestor 会计入 RenderTransform）。
            // 期望：t=0 时 y≈+56dip、scale=0.985、opacity=0；t=280ms 时全部到位。
            try
            {
                var dispatcher = AxolotlMotion.TraceValues ? page.Dispatcher : null;
                var visualParent = page.Parent as System.Windows.Media.Visual
                                   ?? (VisualTreeHelper.GetParent(page) as System.Windows.Media.Visual);
                if (dispatcher != null)
                {
                    foreach (double at in new[] { 0.0, 140.0, 280.0 })
                    {
                        System.Windows.Threading.DispatcherTimer? sample = null;
                        sample = new System.Windows.Threading.DispatcherTimer(
                            TimeSpan.FromMilliseconds(at),
                            System.Windows.Threading.DispatcherPriority.Render,
                            (_, __) =>
                            {
                                try { sample?.Stop(); } catch { }
                                try
                                {
                                    string pos = "n/a";
                                    if (visualParent != null)
                                    {
                                        try
                                        {
                                            var pt = page.TransformToAncestor(visualParent).Transform(new Point(0, 0));
                                            pos = $"renderY={pt.Y:0.0}";
                                        }
                                        catch { }
                                    }
                                    string scaleText = scale != null
                                        ? scale.ScaleX.ToString("0.000") + "@" + MotionAssist.DescribeElement(scaleHost)
                                        : "n/a(no-host)";
                                    TsuruLauncher.Utilities.Logger.LogInfo(
                                        $"[MotionPerf] page-enter sample t={at:0}ms opacity={page.Opacity:0.000} " +
                                        $"translateY={translate.Y:0.0}dip scaleX={scaleText} {pos} " +
                                        $"(want t0: y=+{AxolotlMotion.PageEnterSlidePx} scale={AxolotlMotion.PageEnterScaleFrom} opacity=0)");
                                }
                                catch { }
                            },
                            dispatcher);
                        sample.Start();
                    }
                }
            }
            catch { }

            return;
        }

        /// <summary>
        /// 宿主回落补偿的最低不透明度 —— <b>0.92 -&gt; 0.86</b>（任务 §1：让换页有明确光影变化）。
        /// 0.92 只有 8% 变暗，在 1089x693 的浅色面板上肉眼几乎测不出。
        /// </summary>
        public const double HostDipOpacity = 0.86;

        /// <summary>切页两拍重叠后的总时长（ms）—— 120 + 120 = 240ms，与新页 280ms 重叠播放。</summary>
        public const double HostOverlapMs = 240.0;

        /// <summary>
        /// 切页两拍的**重叠**宿主补偿 —— 取代原先串行的 <c>PlayHostLeave</c>。
        ///
        /// <b>调用顺序很重要</b>：必须在本帧的 <see cref="PlayPageEnter"/> **之后**调用。
        /// 因为 <see cref="PlayPageEnter"/> 开头的 <see cref="SettleActiveMotion"/> 会把动效集合里
        /// 所有元素立即归位，而本方法会把宿主登记进那个集合 ——
        /// 先调用本方法再调用 PlayPageEnter 的话，刚起的回落补偿会被同一帧清掉
        /// （这正是改前 "host=opacity 1-&gt;0.92-&gt;1" 只存在于日志、画面上完全没有的原因）。
        ///
        /// 旧做法（串行）：宿主 120ms ease 淡出 1→0 → Completed 里才真正换页 →
        /// 新页 180ms 淡入 + 200ms 位移。两拍相加 320ms，且前 120ms 用户只看到画面变暗、
        /// 新内容一动不动 —— 这就是「切页/展开收起很僵硬、感知延迟 140ms+」的来源。
        ///
        /// 新做法（重叠）：换页**立即**发生（MainWindow 不再拦 Navigating），
        /// 同一帧里同时启动
        ///   ① 宿主 <c>PageTransitionHost</c> 的 8% 回落补偿：
        ///      1 → 0.92（100ms cubic-bezier(0.4,0,0.2,1)）→ 1（100ms 同曲线），共 200ms；
        ///   ② 新页 <c>PlayPageEnter</c>：opacity 0→1（180ms ease）+ translateY ±30→0（200ms ease）。
        /// 新页自己从 opacity 0 起步，所以"换页那一帧"本来就不会被看到；宿主这 8% 的回落
        /// 只是盖住新旧内容交叠时可能出现的亮度跳变。
        /// 总时长 = max(200, 200) = 200ms ≤ 220ms，且 t=0 就开始动 —— 感知延迟 ≈ 0。
        /// 只动 Opacity，不触发布局、不挂任何 BitmapCache。
        /// </summary>
        public static void PlayHostOverlapFade(FrameworkElement? host, Action? onCompleted = null)
        {
            if (host == null) { onCompleted?.Invoke(); return; }

            // 清掉上一轮（连点导航）可能残留的时钟与缓存，保证每次都从 1 起步
            SettleElement(host);

            if (!AnimationsEnabled)
            {
                host.Opacity = 1.0;
                onCompleted?.Invoke();
                return;
            }

            RegisterTarget(host);
            bool finished = false;
            void Finish()
            {
                if (finished) return;
                finished = true;
                host.BeginAnimation(UIElement.OpacityProperty, null);
                host.Opacity = 1.0;
                MotionAssist.EnableBitmapCache(host, false);
                CancelFinalizeTimer(host);
                _motionTargets.Remove(host);
                onCompleted?.Invoke();
            }

            var dip = new DoubleAnimationUsingKeyFrames
            {
                Duration = AxolotlMotion.Ms(HostOverlapMs),
            };
            dip.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            dip.KeyFrames.Add(new EasingDoubleKeyFrame(HostDipOpacity,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(HostOverlapMs / 2.0)))
            { EasingFunction = AxolotlMotion.EaseInOut });
            dip.KeyFrames.Add(new EasingDoubleKeyFrame(1.0,
                KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(HostOverlapMs)))
            { EasingFunction = AxolotlMotion.EaseInOut });

            dip.Completed += (s, e) => Finish();
            host.BeginAnimation(UIElement.OpacityProperty, dip);

            // ── 证据埋点：证明 1 -> 0.86 -> 1 这次真的在跑 ─────────────────────
            // 改前 <see cref="PlayPageEnter"/> 的 SettleActiveMotion 会在同一帧把宿主归位，
            // 这段回落补偿只活在日志里、画面上从来没有出现过。
            // 这里在 25% / 50% / 75% 三个时刻采样宿主的**实际动画值**（读 DP 会拿到当前动画值），
            // 50% 那一笔必须≈ HostDipOpacity，否则说明又被打断了。
            try
            {
                var dispatcher = AxolotlMotion.TraceValues ? host.Dispatcher : null;
                if (dispatcher != null)
                {
                    double t0 = MotionPerf.NowMs;
                    foreach (double frac in new[] { 0.25, 0.5, 0.75 })
                    {
                        double at = HostOverlapMs * frac;
                        System.Windows.Threading.DispatcherTimer? sample = null;
                        sample = new System.Windows.Threading.DispatcherTimer(
                            TimeSpan.FromMilliseconds(at),
                            System.Windows.Threading.DispatcherPriority.Background,
                            (_, __) =>
                            {
                                try { sample?.Stop(); } catch { }
                                try
                                {
                                    TsuruLauncher.Utilities.Logger.LogInfo(
                                        $"[MotionPerf] host-dip sample t={at:0}ms opacity={host.Opacity:0.000} " +
                                        $"(target at 50% = {HostDipOpacity}) elapsed={MotionPerf.NowMs - t0:0}ms");
                                }
                                catch { }
                            },
                            dispatcher);
                        sample.Start();
                    }
                }
            }
            catch { }

            // Completed 兜底：万一时钟没回调，也在 HostOverlapMs 之后复位
            SettleAfter(host, HostOverlapMs, Finish);
        }

        #endregion

        #region Content helpers

        /// <summary>
        /// 列表 / 面板入场 —— Axolotl Settings.vue:766-791 的搜索列表 stagger：
        /// 每一项 <c>opacity 180ms ease, transform 180ms ease</c>，
        /// 起点 <c>translateY(-6px)</c>，延迟 <c>min(index * 28, 168)ms</c>。
        ///
        /// 两处工程约束（防止"越动画越卡"）：
        ///   1. 错峰只作用于<b>前 <see cref="MaxStaggerItems"/> 项</b>，其余直接到位；
        ///   2. 不给这个容器挂 BitmapCache —— 错峰期间子项每帧都在动，
        ///      缓存位图每帧失效重画，比直接绘制还贵（缓存只对"内容静止、只有自身
        ///      RenderTransform 在变"的元素有意义，见 <see cref="SettleElement"/> 的摘除逻辑）。
        /// </summary>
        public static void PlayStaggeredIn(Panel container, double staggerMs = AxolotlMotion.StaggerStepMs)
        {
            if (container == null) return;

            for (int i = 0; i < container.Children.Count; i++)
            {
                if (container.Children[i] is not FrameworkElement child) continue;

                // 每项都先归零：被上一轮打断时残留在 translateY(-6px) 的卡片在这里复位
                SettleElement(child);

                // 错峰限流：只有前 8 项参与错峰（首屏可见的就这几张），
                // 其余直接到位 —— 几百张卡片同时挂动画时钟是纯粹的帧开销。
                if (!AnimationsEnabled || i >= MaxStaggerItems) continue;

                var translate = MotionVisuals.EnsureTranslate(child);
                child.BeginAnimation(UIElement.OpacityProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                child.Opacity = 0;
                translate.Y = AxolotlMotion.StaggerOffsetPx;
                RegisterTarget(child);

                double delayMs = Math.Min(i * staggerMs, AxolotlMotion.StaggerCapMs);
                PlayMotion(child, true, 0, 1, 0, 0,
                    AxolotlMotion.StaggerOffsetPx, 0,
                    AxolotlMotion.Ms(AxolotlMotion.StaggerItemMs), AxolotlMotion.Ease, null,
                    beginTime: TimeSpan.FromMilliseconds(delayMs));
            }
        }

        /// <summary>
        /// 内容 / 模式切换（极简主页 &lt;-&gt; 信息主页、资源页内容区、右栏内容）。
        ///
        /// 旧内容：150ms ease 淡出（global.scss:305/332/347 页面进出那一条 ease；
        ///         区间 120~150ms 取上限），淡出结束后才
        ///         <see cref="UIElement.Visibility"/> = Collapsed —— 不再用 Visibility 硬切。
        /// 新内容：250ms <c>cubic-bezier(0.15, 1.4, 0.64, 0.96)</c>，
        ///         起点 <c>scale(0.96) + translateY(8px) + opacity 0</c>
        ///         （FloatingActionBar.vue:265-283 的过冲曲线 + 0.5rem = 8px 起点）。
        /// 动画期间两个容器短暂共存，缓存**下沉到各自的内层内容容器**（≤1.2MP）后挂上，结束后一并摘掉。
        ///
        /// <para><b>r3</b>：整页根（1,281,280 设备像素）既不 scale 也不挂缓存 —— scale 与缓存都下沉到
        /// 内层内容容器；降不下去时该处只做 opacity + translateY。详见 <see cref="PlayPageEnter"/>。</para>
        /// <paramref name="forward"/> = true 时新内容自下而上浮入，false 时自上而下。
        /// </summary>
        public static void PlayContentSwitch(FrameworkElement? outgoing, FrameworkElement? incoming, bool forward = true)
        {
            double dir = forward ? 1.0 : -1.0;

            // 切到目标元素前先预热它（Background 优先级、100ms 提前量、可关）——
            // 目标那棵根此刻还是 Collapsed，第一次 Visible 要作废布局、整棵子树重新 measure/arrange，
            // 那一步落在切换动画的第一帧里就是掉帧来源。预热在一个 Dispatcher 回调内跑完，不闪帧。
            if (incoming != null && !ReferenceEquals(outgoing, incoming) && ContentSwitchPrewarmEnabled)
                PrewarmTarget(incoming, ContentSwitchPrewarmLeadMs);

            if (outgoing != null && !ReferenceEquals(outgoing, incoming))
            {
                // 连点：先把上一轮（可能已经下沉到内层容器的 scale / 缓存）清干净再开场
                SettleElement(outgoing);

                if (!AnimationsEnabled)
                {
                    outgoing.Opacity = 0;
                    outgoing.Visibility = Visibility.Collapsed;
                }
                else
                {
                    RegisterTarget(outgoing);
                    // r3：缓存下沉到内层内容容器（≤1.2MP）；降不下去就明确不挂（该处也不做 scale）
                    CacheDuring(outgoing, AxolotlMotion.ContentSwitchLeaveMs, reason: "content-switch-out");
                    double from = outgoing.Opacity <= 0.001 ? 1.0 : outgoing.Opacity;
                    PlayMotion(outgoing, true, from, 0, 0, 0, 0, 0,
                        AxolotlMotion.Ms(AxolotlMotion.ContentSwitchLeaveMs), AxolotlMotion.Ease,
                        () => outgoing.Visibility = Visibility.Collapsed);
                }
            }

            if (incoming == null) return;

            StopActive(incoming);                 // 连点：先停掉上一轮还挂在同一个元素上的 Storyboard
            incoming.Visibility = Visibility.Visible;

            if (!AnimationsEnabled)
            {
                SettleElement(incoming);
                incoming.Opacity = 1;
                return;
            }

            // ── r3：进场也"根不 scale" ────────────────────────────────────────────
            // 与改前（PlayPopup(incoming, …)）**同参数**：opacity 0->1 / scale 0.95->1 /
            // translateY ±16px->0，全是 250ms cubic-bezier(0.15,1.4,0.64,0.96)。
            // 唯一的区别是 scale 的作用对象：不再是被动效的那棵内容根（1,281,280 设备像素，
            // 逐帧 scale = 渲染线程逐帧重新栅格化整棵子树），而是内层内容容器（≤1.2MP）
            // ——并且给**同一个容器**挂了动效缓存，于是缩放只是对一张位图做重采样。
            // 内层容器也降不到 1.2MP 以下时 scaleFrom/scaleTo 传 null：该处不 scale、不缓存。
            var enterHost = MotionAssist.ResolveScaleHost(incoming, "content-switch-in", out double enterHostPx);
            CacheDuring(incoming, AxolotlMotion.ContentSwitchMs, enterHost, enterHostPx, "content-switch-in");

            PlayMotion(incoming, true, 0, 1, 0, 0,
                dir * AxolotlMotion.ContentSwitchOffsetPx, 0,
                AxolotlMotion.Ms(AxolotlMotion.ContentSwitchMs), AxolotlMotion.OvershootEase, null,
                slideDuration: AxolotlMotion.Ms(AxolotlMotion.ContentSwitchMs),
                slideEase: AxolotlMotion.OvershootEase,
                scaleFrom: enterHost != null ? AxolotlMotion.ContentSwitchFromScale : (double?)null,
                scaleTo: enterHost != null ? 1.0 : (double?)null,
                scaleDuration: AxolotlMotion.Ms(AxolotlMotion.ContentSwitchMs),
                scaleEase: AxolotlMotion.OvershootEase,
                scaleHost: enterHost);
        }

        /// <summary>
        /// 内容区淡出到指定不透明度 —— 类别切换时先把旧结果淡掉，
        /// 等新结果到了再用 <see cref="PlayContentFadeIn"/> 淡入 + 错峰，
        /// 这样「旧内容先离场、新内容再进场」，不会新旧糊在一起。
        /// 时长取页面离开那一组 120ms ease-in（global.scss:347）。
        /// </summary>
        public static void PlayContentFadeOut(FrameworkElement? root, Action? onCompleted = null)
        {
            if (root == null) { onCompleted?.Invoke(); return; }
            StopActive(root);

            if (!AnimationsEnabled)
            {
                root.BeginAnimation(UIElement.OpacityProperty, null);
                root.Opacity = 0;
                onCompleted?.Invoke();
                return;
            }

            CacheDuring(root, AxolotlMotion.PageLeaveMs);
            PlayMotion(root, true, root.Opacity, 0,
                0, 0, 0, 0,
                AxolotlMotion.Ms(AxolotlMotion.PageLeaveMs), AxolotlMotion.EaseIn, onCompleted);
        }

        /// <summary>
        /// 纯不透明度过渡（不动位移、不动缩放）—— 资源页左栏折叠时文字 150ms 淡出、
        /// 展开时延迟 80ms 再 150ms 淡入（落在宽度动画的后段，不会被挤压变形）。
        ///
        /// 与 <see cref="PlayContentFadeOut"/> 的区别：这里要能带 <c>BeginTime</c> 延迟，
        /// 而且落终态走 <see cref="SettleAfter"/> 兜底定时器（Completed 没来也不会卡在半透明），
        /// 保证「动画结束必须强制归位」。
        /// 不挂 BitmapCache：文字容器本身内容静止但太小，挂缓存纯属浪费显存。
        /// </summary>
        public static void PlayOpacity(FrameworkElement? element, double toOpacity, double ms,
            IEasingFunction ease, double delayMs = 0, Action? onCompleted = null)
        {
            if (element == null) { onCompleted?.Invoke(); return; }
            try
            {
                StopActive(element);

                double from = element.Opacity;
                if (!AnimationsEnabled || Math.Abs(toOpacity - from) < 0.001)
                {
                    SettleOpacity(element, toOpacity);
                    onCompleted?.Invoke();
                    return;
                }

                element.BeginAnimation(UIElement.OpacityProperty, null);
                RegisterTarget(element);

                var anim = new DoubleAnimation(from, toOpacity, AxolotlMotion.Ms(ms)) { EasingFunction = ease };
                if (delayMs > 0) anim.BeginTime = TimeSpan.FromMilliseconds(delayMs);
                element.BeginAnimation(UIElement.OpacityProperty, anim);

                SettleAfter(element, ms + delayMs, () =>
                {
                    SettleOpacity(element, toOpacity);
                    onCompleted?.Invoke();
                });
            }
            catch { }
        }

        /// <summary>
        /// 内容区整体的进场（资源类别切换 / 搜索结果刷新 / 版本设置标签切换）。
        ///
        /// 改前：纯淡入 <c>opacity -&gt; 1 / 180ms ease</c> —— 没有任何位移或缩放，
        /// 在一整块本来就是同色底的内容区上几乎看不出「换了一批内容」。
        ///
        /// 改后（任务 §5：位移 &gt;= 12px、scale 0.94~0.96 -&gt; 1、200~250ms、透明度同步）：
        ///   * opacity    from -&gt; 1              220ms ease（单调曲线，不会因为过冲在 1.0 上抖）
        ///   * translateY +16px -&gt; 0            250ms cubic-bezier(0.15, 1.4, 0.64, 0.96)
        ///   * scale      0.95 -&gt; 1             250ms 同曲线
        /// 逐项错峰交给 <see cref="PlayStaggeredIn"/>（40ms 步长 / 200ms 上限 / 220ms ease / translateY(-8px)）。
        /// 只动 Opacity / RenderTransform。
        ///
        /// <para><b>r3</b>：<paramref name="root"/> 本身**不再 scale** —— scale 下沉到面积 ≤ 1.2MP 的
        /// 内层内容容器，并给同一个容器挂动效缓存；降不下去就整段不做 scale（只留 opacity + translateY）。
        /// 详见 <see cref="PlayPageEnter"/> 的根因说明。</para>
        /// </summary>
        public static void PlayContentFadeIn(FrameworkElement? root, bool staggerChildren = true,
            double staggerMs = AxolotlMotion.StaggerStepMs,
            double offsetY = AxolotlMotion.ContentSwitchOffsetPx,
            double fromScale = AxolotlMotion.ContentSwitchFromScale)
        {
            if (root == null) return;
            StopActive(root);

            if (staggerChildren && root is Panel panel)
                PlayStaggeredIn(panel, staggerMs);

            if (!AnimationsEnabled)
            {
                SettleElement(root);   // 清时钟 + Opacity=1 + 位移清零 + 缩放写回 1.0 + 摘缓存
                return;
            }

            double from = root.Opacity <= 0.001 ? 0.0 : root.Opacity;
            if (from >= 1.0) from = 0.0;
            RegisterTarget(root);

            // r3：缓存与 scale 一起下沉到内层内容容器（≤1.2MP）。
            // 内层容器降不下去时 scaleHost==null —— 这一处就"不缓存也不 scale"，只留 opacity + translateY。
            // 资源页整页视图（PlayFullPageTransition）走的就是这里。
            var scaleHost = MotionAssist.ResolveScaleHost(root, "content-fade-in", out double scaleHostPx);
            CacheDuring(root, AxolotlMotion.ContentSwitchMs, scaleHost, scaleHostPx, "content-fade-in");

            PlayMotion(root, true, from, 1, 0, 0, offsetY, 0,
                AxolotlMotion.Ms(AxolotlMotion.PageEnterFadeMs), AxolotlMotion.Ease, null,
                slideDuration: AxolotlMotion.Ms(AxolotlMotion.ContentSwitchMs),
                slideEase: AxolotlMotion.OvershootEase,
                scaleFrom: scaleHost != null ? fromScale : (double?)null,
                scaleTo: scaleHost != null ? 1.0 : (double?)null,
                scaleDuration: AxolotlMotion.Ms(AxolotlMotion.ContentSwitchMs),
                scaleEase: AxolotlMotion.OvershootEase,
                scaleHost: scaleHost);
        }

        /// <summary>
        /// 弹窗开 / 关（遮罩 + 面板）—— NewModal / PopupInEase 那一组：
        /// 面板 250ms <c>cubic-bezier(0.51, 1.08, 0.35, 1.15)</c>（App.vue:3592），
        /// 起点 <c>scale(0.96) + translateY(24px) + opacity 0</c>；
        /// 离场 250ms <c>cubic-bezier(0.68, -0.17, 0.23, 0.11)</c>（App.vue:3599）；
        /// 遮罩 200ms ease-out / ease-in（NewModal.vue:68-108），终态 0.72。
        /// </summary>
        public static void PlayModal(FrameworkElement? mask, FrameworkElement? panel, bool show,
            Action? onClosed = null, double maskOpacity = 1.0)
        {
            double maskMs = AxolotlMotion.ModalMaskMs;
            double panelMs = AxolotlMotion.ModalPanelMs;
            IEasingFunction panelEase = show ? AxolotlMotion.PopupInEase : AxolotlMotion.PopupOutEase;
            IEasingFunction maskEase = show ? AxolotlMotion.EaseOut : AxolotlMotion.EaseIn;

            if (mask != null)
            {
                StopActive(mask);

                if (!AnimationsEnabled)
                {
                    mask.BeginAnimation(UIElement.OpacityProperty, null);
                    mask.Opacity = show ? maskOpacity : 0.0;
                }
                else
                {
                    PlayMotion(mask, true, show ? 0.0 : mask.Opacity, show ? maskOpacity : 0.0, 0, 0, 0, 0,
                        AxolotlMotion.Ms(maskMs), maskEase, null);
                }
            }

            if (panel == null)
            {
                onClosed?.Invoke();
                return;
            }

            StopActive(panel);

            if (!AnimationsEnabled)
            {
                var sc0 = MotionVisuals.EnsureScale(panel);
                var tr0 = MotionVisuals.EnsureTranslate(panel);
                Reset(panel, sc0, tr0);
                panel.Opacity = show ? 1.0 : 0.0;
                sc0.ScaleX = sc0.ScaleY = 1.0;
                tr0.X = 0; tr0.Y = 0;
                onClosed?.Invoke();
                return;
            }

            PlayPopup(panel, show,
                AxolotlMotion.ModalPanelFromScale,
                panelMs,
                panelEase,
                AxolotlMotion.ModalPanelOffsetPx,
                onClosed);
        }

        /// <summary>
        /// 创建实例向导开 / 关（DownloadPage 页面内嵌的遮罩 + 面板）。
        ///
        /// 打开：遮罩 <c>opacity 0 -&gt; maskOpacity</c> 150ms ease-out（浮层遮罩那一组）；
        ///       面板 250ms <c>cubic-bezier(0.15, 1.4, 0.64, 0.96)</c>（真过冲），
        ///       起点 <c>scale(0.96) + translateY(12px) + opacity 0</c>。
        /// 关闭：遮罩与面板都反向 150ms ease。
        /// 全程只动 Opacity / RenderTransform；动画结束后才由 <paramref name="onHidden"/>
        /// 把整层 Visibility 落成 Collapsed —— 不用 Visibility 当动画主体。
        /// </summary>
        public static void PlayWizard(FrameworkElement? mask, FrameworkElement? panel, bool show,
            Action? onHidden = null, double maskOpacity = AxolotlMotion.ModalMaskOpacity)
        {
            if (mask != null)
            {
                StopActive(mask);

                if (!AnimationsEnabled)
                {
                    mask.BeginAnimation(UIElement.OpacityProperty, null);
                    mask.Opacity = show ? maskOpacity : 0.0;
                }
                else
                {
                    CacheDuring(mask, AxolotlMotion.OverlayMaskMs);
                    PlayMotion(mask, true, show ? 0.0 : mask.Opacity, show ? maskOpacity : 0.0, 0, 0, 0, 0,
                        AxolotlMotion.Ms(AxolotlMotion.OverlayMaskMs),
                        show ? AxolotlMotion.EaseOut : AxolotlMotion.EaseIn, null);
                }
            }

            if (panel == null)
            {
                onHidden?.Invoke();
                return;
            }

            double panelMs = show ? AxolotlMotion.ContentSwitchMs : AxolotlMotion.WizardPanelLeaveMs;
            PlayPopup(panel, show,
                AxolotlMotion.ContentSwitchFromScale,
                panelMs,
                show ? AxolotlMotion.OvershootEase : AxolotlMotion.Ease,
                AxolotlMotion.WizardPanelOffsetPx,
                onHidden);
        }

        /// <summary>
        /// 展开 / 收起 —— Axolotl CollapsibleAdmonition.vue:176-190：
        /// <c>opacity 300ms ease-in-out, transform 300ms ease-in-out</c>，
        /// 两端都是 <c>translateY(-10px)</c>。
        /// </summary>
        public static void PlayExpandCollapse(FrameworkElement target, bool expand, Action? onCompleted = null)
        {
            if (target == null) { onCompleted?.Invoke(); return; }
            StopActive(target);

            if (!AnimationsEnabled)
            {
                ApplyRestingState(target, expand ? 1 : target.Opacity, 0, 0);
                onCompleted?.Invoke();
                return;
            }

            const double D = AxolotlMotion.CollapseOffsetPx;
            if (expand)
            {
                PlayMotion(target, true, 0, 1, 0, 0, -D, 0,
                    AxolotlMotion.Ms(AxolotlMotion.CollapseMs), AxolotlMotion.EaseInOut, onCompleted);
            }
            else
            {
                PlayMotion(target, true, 1, 0, 0, 0, 0, -D,
                    AxolotlMotion.Ms(AxolotlMotion.CollapseMs), AxolotlMotion.EaseInOut, onCompleted);
            }
        }

        /// <summary>
        /// 右下悬浮胶囊 —— Axolotl FloatingActionBar.vue:265-283：
        /// 入场 <c>transform/opacity 0.25s cubic-bezier(0.15, 1.4, 0.64, 0.96)</c>，
        /// 起点 <c>scale(0.5) translateY(-10rem)</c>（= -160px，会明显过冲回弹）；
        /// 离场 <c>0.25s ease</c>，终点 <c>scale(0.96) translateY(-0.25rem)</c>（= -4px）。
        /// </summary>
        public static void PlayFloatingBar(FrameworkElement target, bool show, Action? onCompleted = null)
        {
            if (target == null) { onCompleted?.Invoke(); return; }
            StopActive(target);

            var scale = MotionVisuals.EnsureScale(target);
            var translate = MotionVisuals.EnsureTranslate(target);

            if (!AnimationsEnabled)
            {
                target.BeginAnimation(UIElement.OpacityProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                target.Opacity = show ? 1 : 0;
                scale.ScaleX = scale.ScaleY = 1.0;
                translate.Y = 0;
                onCompleted?.Invoke();
                return;
            }

            var sb = new Storyboard();
            double ms = show ? AxolotlMotion.FloatingBarEnterMs : AxolotlMotion.FloatingBarLeaveMs;
            IEasingFunction ease = show ? AxolotlMotion.OvershootEase : AxolotlMotion.Ease;

            double opacityFrom = show ? 0 : target.Opacity;
            double opacityTo = show ? 1 : 0;
            double scaleFrom = show ? AxolotlMotion.FloatingBarEnterScale : 1.0;
            double scaleTo = show ? 1.0 : AxolotlMotion.FloatingBarLeaveScale;
            double yFrom = show ? AxolotlMotion.FloatingBarEnterOffsetPx : 0.0;
            double yTo = show ? 0.0 : AxolotlMotion.FloatingBarLeaveOffsetPx;

            Reset(target, scale, translate);
            target.Opacity = opacityFrom;
            scale.ScaleX = scaleFrom; scale.ScaleY = scaleFrom;
            translate.Y = yFrom;

            AddFade(sb, target, opacityFrom, opacityTo, ms, ease);
            AddScale(sb, scale, scaleFrom, scaleTo, ms, ease);
            AddTranslateY(sb, translate, yFrom, yTo, ms, ease);

            // 与 PlayPopup 同一套收尾：终态只落一次 + Completed 没来也有兜底定时器。
            int token = NextPlayToken(target);
            bool finished = false;
            void Finish()
            {
                if (finished) return;
                finished = true;
                if (!IsCurrentPlay(target, token)) return;
                FinalizeElement(target, true, opacityTo, 0, yTo, scaleTo);
                try { onCompleted?.Invoke(); } catch { }
            }

            Begin(sb, target, Finish);
            if (sb.Children.Count > 0) sb.Begin(); else Finish();
            SettleAfter(target, ms, Finish);
        }

        /// <summary>
        /// 弹窗 / 下拉 / 悬浮面板 —— 按原版各自的数值：
        /// <paramref name="fromScale"/> 由调用方给（下拉 0.75、FloatingPanel 0.85、预览卡 0.98）。
        /// </summary>
        public static void PlayPopup(FrameworkElement target, bool show, double fromScale, double durationMs,
            IEasingFunction? ease = null, double offsetY = 0, Action? onCompleted = null, bool useCache = true)
        {
            if (target == null) { onCompleted?.Invoke(); return; }
            StopActive(target);

            var scale = MotionVisuals.EnsureScale(target);
            var translate = MotionVisuals.EnsureTranslate(target);
            IEasingFunction curve = ease ?? AxolotlMotion.EaseInOut;

            if (!AnimationsEnabled)
            {
                Reset(target, scale, translate);
                target.Opacity = show ? 1 : 0;
                scale.ScaleX = scale.ScaleY = 1.0;
                translate.Y = 0;
                MotionAssist.EnableBitmapCache(target, false);
                onCompleted?.Invoke();
                return;
            }

            double opacityFrom = show ? 0 : target.Opacity;
            double opacityTo = show ? 1 : 0;
            double scaleFrom = show ? fromScale : 1.0;
            double scaleTo = show ? 1.0 : fromScale;
            double yFrom = show ? offsetY : 0.0;
            double yTo = show ? 0.0 : offsetY;

            // 动画期间给容器挂 BitmapCache，结束后由 CacheDuring 的定时器摘掉（避免文字长期发虚）。
            // useCache:false 用于「收尾要切 Visibility」的那一侧 —— 缓存 + 布局变更会让渲染线程崩
            // （见 MotionAssist.CacheDisabled 的说明），宁可不缓存也不能崩。
            if (useCache) CacheDuring(target, durationMs);

            Reset(target, scale, translate);
            target.Opacity = opacityFrom;
            scale.ScaleX = scaleFrom; scale.ScaleY = scaleFrom;
            translate.Y = yFrom;

            // ── 收尾：终态只落一次，而且只允许「当前这一轮」落 ─────────────────────
            //   * Storyboard.Completed 正常到达        → 立刻落终态（并作废兜底定时器）；
            //   * Completed 因为任何原因没来（动画时钟没被渲染线程推进 / 时钟被清掉 /
            //     被新一轮播放接管）→ SettleAfter 在 durationMs+40ms 强制落地。
            //
            // 改前这里**只有** Completed 一条路：一旦它不来，元素就永远停在中间不透明度，
            // 而调用方写在 onCompleted 里的 Visibility 切换也永远不会执行 ——
            // 覆盖层于是以半透明/不透明状态一直盖在列表上，吞掉所有点击。
            // 这就是「进工具后覆盖层停在半透明状态、整页发白卡住」的直接成因。
            // 同文件里 PlayMotion 早就有这层兜底（见其 SettleAfter 调用），PlayPopup 漏了。
            int token = NextPlayToken(target);
            bool finished = false;
            void Finish()
            {
                if (finished) return;
                finished = true;
                if (!IsCurrentPlay(target, token)) return;   // 已被新一轮接管：不要用旧终态覆盖新动画
                MotionPerf.Mark("popup-finish " + MotionAssist.DescribeElement(target));
                FinalizeElement(target, true, opacityTo, 0, yTo, scaleTo);
                try { onCompleted?.Invoke(); } catch { }
            }

            var sb = new Storyboard();
            AddFade(sb, target, opacityFrom, opacityTo, durationMs, curve);
            AddScale(sb, scale, scaleFrom, scaleTo, durationMs, curve);
            if (Math.Abs(yTo - yFrom) > 0.01) AddTranslateY(sb, translate, yFrom, yTo, durationMs, curve);

            Begin(sb, target, Finish);
            if (FaultFreezePopup)
            {
                // 故障注入：不启动 Storyboard（= 动画时钟不推进），停在中间不透明度上。
                target.Opacity = (opacityFrom + opacityTo) * 0.5;
                try
                {
                    TsuruLauncher.Utilities.Logger.LogInfo(
                        "[MotionFault] popup frozen mid-flight show=" + show +
                        " opacity=" + target.Opacity.ToString("0.###") +
                        " watchdog=" + FinalizeWatchdogEnabled);
                }
                catch { }
            }
            // 三条动画都被优化掉（|to-from| < 0.0001）时 Storyboard 是空的：
            // 空 Storyboard 的 Duration=Automatic 对没有子项的时钟等于 Forever，Completed 永远不来。
            else if (sb.Children.Count > 0) sb.Begin();
            else Finish();

            // 兜底：Completed 没来也必须落终态、清时钟、摘缓存、切 Visibility。
            // TSURU_MOTION_WATCHDOG=0 时退化成改前的行为（只靠 Completed），用于 A/B 对照。
            if (TransitionWatchdogEnabled) SettleAfter(target, durationMs, Finish);
        }

        /// <summary>
        /// 导航按钮入场 —— App.vue:3611-3653 <c>all 0.5s cubic-bezier(0.15, 1.4, 0.64, 0.96)</c>，
        /// 起点 <c>scale: 0.5; translate: -2rem 0; opacity: 0</c>。
        /// </summary>
        public static void PlayNavItemIn(FrameworkElement target)
        {
            if (target == null) return;
            StopActive(target);

            var scale = MotionVisuals.EnsureScale(target);
            var translate = MotionVisuals.EnsureTranslate(target);

            if (!AnimationsEnabled)
            {
                Reset(target, scale, translate);
                target.Opacity = 1;
                scale.ScaleX = scale.ScaleY = 1.0;
                translate.X = 0;
                return;
            }

            Reset(target, scale, translate);
            target.Opacity = 0;
            scale.ScaleX = scale.ScaleY = AxolotlMotion.NavButtonEnterScale;
            translate.X = AxolotlMotion.NavButtonEnterOffsetPx;

            double ms = AxolotlMotion.NavButtonEnterMs;
            var sb = new Storyboard();
            AddFade(sb, target, 0, 1, ms, AxolotlMotion.OvershootEase);
            AddScale(sb, scale, AxolotlMotion.NavButtonEnterScale, 1.0, ms, AxolotlMotion.OvershootEase);
            AddTranslateX(sb, translate, AxolotlMotion.NavButtonEnterOffsetPx, 0, ms, AxolotlMotion.OvershootEase);

            int token = NextPlayToken(target);
            bool finished = false;
            void Finish()
            {
                if (finished) return;
                finished = true;
                if (!IsCurrentPlay(target, token)) return;
                FinalizeElement(target, true, 1.0, 0, 0, 1.0);
            }

            Begin(sb, target, Finish);
            if (sb.Children.Count > 0) sb.Begin(); else Finish();
            SettleAfter(target, ms, Finish);
        }

        /// <summary>
        /// 导航按钮离场 —— App.vue:3614-3652 <c>all 0.25s ease</c>，
        /// 终点 <c>scale: 0.75; opacity: 0</c>。
        /// </summary>
        public static void PlayNavItemOut(FrameworkElement target, Action? onCompleted = null)
        {
            if (target == null) { onCompleted?.Invoke(); return; }
            StopActive(target);

            var scale = MotionVisuals.EnsureScale(target);

            if (!AnimationsEnabled)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                target.BeginAnimation(UIElement.OpacityProperty, null);
                target.Opacity = 0;
                scale.ScaleX = scale.ScaleY = AxolotlMotion.NavButtonLeaveScale;
                onCompleted?.Invoke();
                return;
            }

            double ms = AxolotlMotion.NavButtonLeaveMs;
            var sb = new Storyboard();
            AddFade(sb, target, target.Opacity, 0, ms, AxolotlMotion.Ease);
            AddScale(sb, scale, scale.ScaleX, AxolotlMotion.NavButtonLeaveScale, ms, AxolotlMotion.Ease);

            // 收尾同上：离场终态是 Opacity=0 / scale=0.75（不是静止态），
            // 但**必须**保证落地 —— 否则按钮会永远停在中途的缩放/半透明上。
            int token = NextPlayToken(target);
            bool finished = false;
            void Finish()
            {
                if (finished) return;
                finished = true;
                if (!IsCurrentPlay(target, token)) return;
                try
                {
                    target.BeginAnimation(UIElement.OpacityProperty, null);
                    target.Opacity = 0;
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scale.ScaleX = scale.ScaleY = AxolotlMotion.NavButtonLeaveScale;
                    MotionAssist.EnableBitmapCache(target, false);
                    CancelFinalizeTimer(target);
                    _motionTargets.Remove(target);
                }
                catch { }
                try { onCompleted?.Invoke(); } catch { }
            }

            Begin(sb, target, Finish);
            if (sb.Children.Count > 0) sb.Begin(); else Finish();
            SettleAfter(target, ms, Finish);
        }

        /// <summary>
        /// 步骤翻页（创建实例向导）—— SymlinkMethodCards.vue:1383-1450：
        /// 出 150ms cubic-bezier(0.4, 0, 1, 1)，入 170ms cubic-bezier(0, 0, 0.2, 1)，
        /// 位移 <c>+-100%</c>（按元素宽度换算）。
        /// </summary>
        public static void PlayStepTransition(FrameworkElement outgoing, FrameworkElement incoming, bool forward)
        {
            double sign = forward ? 1.0 : -1.0;

            if (outgoing != null)
            {
                double w = outgoing.ActualWidth > 0 ? outgoing.ActualWidth : 640;
                PlayMotion(outgoing, true, 1, 0, 0, -sign * w, 0, 0,
                    AxolotlMotion.Ms(AxolotlMotion.StepOutMs), AxolotlMotion.StepOutEase, null);
            }

            if (incoming != null)
            {
                double w = incoming.ActualWidth > 0 ? incoming.ActualWidth : 640;
                PlayMotion(incoming, true, 0, 1, sign * w, 0, 0, 0,
                    AxolotlMotion.Ms(AxolotlMotion.StepInMs), AxolotlMotion.StepInEase, null);
            }
        }

        #endregion

        #region Sidebar width（Axolotl App.vue:3376-3380）

        /// <summary>
        /// 右栏展开 / 收起 —— Axolotl 用 <c>transition: --right-bar-width 320ms cubic-bezier(0.22, 1, 0.36, 1)</c>
        /// 直接过渡 <c>grid-template-columns: 1fr var(--right-bar-width)</c>。
        /// WPF 的等价物就是直接动画 <see cref="ColumnDefinition.Width"/>（像素 GridLength），
        /// 面板内容本身保持固定宽度并被 ClipToBounds 裁切，因此不会出现「内容被压扁」的抖动。
        /// </summary>
        public static void AnimateSidebarWidth(ColumnDefinition column, FrameworkElement? panel, double toWidth,
            Action? onCompleted = null)
        {
            if (column == null) { onCompleted?.Invoke(); return; }

            double from = column.ActualWidth > 0.5
                ? column.ActualWidth
                : (column.Width.IsAbsolute ? column.Width.Value : 0.0);

            if (!AnimationsEnabled || Math.Abs(toWidth - from) < 0.5)
            {
                column.BeginAnimation(ColumnDefinition.WidthProperty, null);
                column.Width = new GridLength(toWidth);
                if (panel != null) MotionAssist.EnableBitmapCache(panel, false);
                onCompleted?.Invoke();
                return;
            }

            // 动画期间给面板挂 BitmapCache，减少每帧重绘；结束后摘掉，避免文字发虚。
            if (panel != null) MotionAssist.EnableBitmapCache(panel, true);

            var anim = new GridLengthAnimation
            {
                From = new GridLength(from),
                To = new GridLength(toWidth),
                Duration = AxolotlMotion.Ms(AxolotlMotion.SidebarWidthMs),
                EasingFunction = AxolotlMotion.SidebarEase,
            };

            // 收尾必须幂等：Completed 与 SettleAfter 兜底可能都会到（谁先到谁生效）。
            // 渲染线程一旦失效 Completed 永不触发（见 HANDOFF §6.6），所以兜底定时器不能省。
            bool done = false;
            void Finish()
            {
                if (done) return;
                done = true;
                column.BeginAnimation(ColumnDefinition.WidthProperty, null);
                column.Width = new GridLength(toWidth);
                if (panel != null) MotionAssist.EnableBitmapCache(panel, false);
                onCompleted?.Invoke();
            }

            anim.Completed += (s, e) => Finish();
            column.BeginAnimation(ColumnDefinition.WidthProperty, anim);

            if (panel != null) SettleAfter(panel, AxolotlMotion.SidebarWidthMs, Finish);
        }

        /// <summary>
        /// 元素自身宽度的展开 / 收起动画（资源页左侧栏 212 与 64 之间切换）。
        ///
        /// 和 <see cref="AnimateSidebarWidth"/> 同一套机制（同一条
        /// <c>cubic-bezier(0.22, 1, 0.36, 1)</c>、动画期挂 BitmapCache、结束强制定格并摘掉），
        /// 区别是直接动画 <see cref="FrameworkElement.WidthProperty"/>：
        /// 左侧栏在 <c>Width="Auto"</c> 的列里，没有 ColumnDefinition 可以动画，
        /// 而且卡片 <c>Visibility</c> 切到 Collapsed 时列宽必须能跟着归零（整页浮层场景），
        /// 所以不能把列改成固定宽度。
        /// 只动 Width —— 不碰 Margin（硬性要求：禁止动画 Margin）。
        /// </summary>
        public static void AnimateElementWidth(FrameworkElement? element, double toWidth, double durationMs,
            IEasingFunction ease, Action? onCompleted = null)
        {
            if (element == null) { onCompleted?.Invoke(); return; }

            double from = element.ActualWidth > 0.5
                ? element.ActualWidth
                : (double.IsNaN(element.Width) ? toWidth : element.Width);

            if (!AnimationsEnabled || Math.Abs(toWidth - from) < 0.5)
            {
                element.BeginAnimation(FrameworkElement.WidthProperty, null);
                element.Width = toWidth;
                MotionAssist.EnableBitmapCache(element, false);
                onCompleted?.Invoke();
                return;
            }

            // 动画期间挂缓存（MotionAssist 内部按 1.2MP 设备像素上限自行决定挂 / 跳过）
            MotionAssist.EnableBitmapCache(element, true);

            var anim = new DoubleAnimation(from, toWidth, AxolotlMotion.Ms(durationMs)) { EasingFunction = ease };
            element.BeginAnimation(FrameworkElement.WidthProperty, anim);

            // 收尾统一交给兜底定时器（Completed 不来也不会停在半宽）：
            // 清时钟 + 写到终值 + 摘缓存，不留 HoldEnd 残留。
            SettleAfter(element, durationMs, () =>
            {
                element.BeginAnimation(FrameworkElement.WidthProperty, null);
                element.Width = toWidth;
                MotionAssist.EnableBitmapCache(element, false);
                onCompleted?.Invoke();
            });
        }

        #endregion

        #region Skeleton / shimmer（LoadingIndicator.vue:57-129）

        /// <summary>
        /// 骨架屏脉冲：<c>animation: pop 4s ease-in-out infinite</c>，
        /// 0% opacity .25 / 50% opacity .5 / 100% opacity .25，
        /// 每一行依次延迟 0s / 0.3s / 0.6s（LoadingIndicator.vue:81-103）。
        /// </summary>
        public static void PlaySkeletonPulse(Panel container)
        {
            if (container == null) return;
            if (!AnimationsEnabled) return;

            for (int i = 0; i < container.Children.Count; i++)
            {
                if (container.Children[i] is not FrameworkElement child) continue;

                var anim = new DoubleAnimationUsingKeyFrames
                {
                    Duration = AxolotlMotion.Ms(AxolotlMotion.SkeletonMs),
                    RepeatBehavior = RepeatBehavior.Forever,
                    BeginTime = TimeSpan.FromMilliseconds((i % 3) * AxolotlMotion.SkeletonStepDelayMs),
                };
                anim.KeyFrames.Add(new EasingDoubleKeyFrame(AxolotlMotion.SkeletonDimOpacity,
                    KeyTime.FromPercent(0.0)) { EasingFunction = AxolotlMotion.EaseInOut });
                anim.KeyFrames.Add(new EasingDoubleKeyFrame(AxolotlMotion.SkeletonBrightOpacity,
                    KeyTime.FromPercent(0.5)) { EasingFunction = AxolotlMotion.EaseInOut });
                anim.KeyFrames.Add(new EasingDoubleKeyFrame(AxolotlMotion.SkeletonDimOpacity,
                    KeyTime.FromPercent(1.0)) { EasingFunction = AxolotlMotion.EaseInOut });

                child.BeginAnimation(UIElement.OpacityProperty, anim);
            }
        }

        /// <summary>停止骨架屏脉冲并把不透明度还给 XAML。</summary>
        public static void StopSkeleton(Panel container)
        {
            if (container == null) return;
            foreach (var c in container.Children)
            {
                if (c is FrameworkElement fe) fe.BeginAnimation(UIElement.OpacityProperty, null);
            }
        }

        /// <summary>
        /// shimmer 扫光：<c>4s ease-in-out infinite</c>，<c>translateX(-80%) -> translateX(80%)</c>。
        /// <paramref name="extent"/> 一般传 shimmer 覆盖层的宽度。
        /// </summary>
        public static void PlayShimmer(FrameworkElement shimmer, double extent)
        {
            if (shimmer == null || extent <= 0) return;
            if (!AnimationsEnabled) return;

            var translate = MotionVisuals.EnsureTranslate(shimmer);
            var anim = new DoubleAnimation(AxolotlMotion.ShimmerFromRatio * extent, AxolotlMotion.ShimmerToRatio * extent,
                AxolotlMotion.Ms(AxolotlMotion.ShimmerMs))
            {
                EasingFunction = AxolotlMotion.EaseInOut,
                RepeatBehavior = RepeatBehavior.Forever,
            };
            translate.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        /// <summary>脉冲光环：<c>1.6s ease-out infinite</c>，扩散半径 0.5rem = 8px。</summary>
        public static void PlayRingPulse(FrameworkElement ring)
        {
            if (ring == null || !AnimationsEnabled) return;
            var scale = MotionVisuals.EnsureScale(ring);
            var anim = new DoubleAnimation(1.0, 1.0 + AxolotlMotion.RingPulseRadiusPx / 24.0,
                AxolotlMotion.Ms(AxolotlMotion.RingPulseMs))
            {
                EasingFunction = AxolotlMotion.EaseOut,
                RepeatBehavior = RepeatBehavior.Forever,
                AutoReverse = false,
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        /// <summary>搜索命中高亮：<c>0.9s ease-in-out 2</c>（Settings.vue:969）。</summary>
        public static void PlayHighlight(FrameworkElement target)
        {
            if (target == null || !AnimationsEnabled) return;

            var anim = new DoubleAnimationUsingKeyFrames
            {
                Duration = AxolotlMotion.Ms(AxolotlMotion.HighlightMs),
                RepeatBehavior = new RepeatBehavior(AxolotlMotion.HighlightRepeat),
            };
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(0.0)));
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(0.35, KeyTime.FromPercent(0.5)) { EasingFunction = AxolotlMotion.EaseInOut });
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0)) { EasingFunction = AxolotlMotion.EaseInOut });
            target.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        /// <summary>float-in：<c>translation-float-in 0.5s ease-out both</c>，起点 <c>translateY(12px)</c>。</summary>
        public static void PlayFloatIn(FrameworkElement target, double delayMs = 0)
        {
            if (target == null) return;
            StopActive(target);

            if (!AnimationsEnabled)
            {
                ApplyRestingState(target, 1, 0, 0);
                return;
            }

            var translate = MotionVisuals.EnsureTranslate(target);
            target.BeginAnimation(UIElement.OpacityProperty, null);
            translate.BeginAnimation(TranslateTransform.YProperty, null);
            target.Opacity = 0;
            translate.Y = AxolotlMotion.FloatInOffsetPx;

            PlayMotion(target, true, 0, 1, 0, 0, AxolotlMotion.FloatInOffsetPx, 0,
                AxolotlMotion.Ms(AxolotlMotion.FloatInMs), AxolotlMotion.EaseOut, null,
                beginTime: delayMs > 0 ? TimeSpan.FromMilliseconds(delayMs) : (TimeSpan?)null);
        }

        #endregion

        #region Storyboard plumbing

        /// <summary>
        /// 动画期间给「容器」挂 <c>CacheMode = BitmapCache</c>（任务要求：只对容器缓存，
        /// 不是每个子项各来一张位图），动画时长 + 40ms 之后自动摘掉，
        /// 避免动画结束还留着缓存让文字发虚。
        /// 同一元素连续触发时只保留最后一个定时器，不会出现「新动画刚开就被上一轮摘掉」。
        ///
        /// <para><b>r3：缓存目标从「整页根」改成「内层内容容器」。</b>
        /// <paramref name="host"/> 是调用方已经解析好的宿主（面积 ≤
        /// <see cref="MotionAssist.MotionHostDevicePixelBudget"/>）：传进来就用它、不再走一遍可视树；
        /// 传 null 时由 <see cref="MotionAssist.AttachMotionCache"/> 现场解析。
        /// <paramref name="element"/> 自己超过 1.2MP 时**绝不挂它自己** ——
        /// 挂整页根的大位图缓存会把渲染线程打崩（帧间隔 1.2s），这是已知事实。
        /// 解析不出 ≤ 1.2MP 的宿主时记一笔 <c>[MotionPerf] cache-target none …</c> 并返回，
        /// 对应"该处不缓存（也不做 scale）"。</para>
        /// </summary>
        private static readonly Dictionary<FrameworkElement, System.Windows.Threading.DispatcherTimer> _cacheTimers = new();

        private static void CacheDuring(FrameworkElement element, double animationMs,
            FrameworkElement? host = null, double hostPx = 0.0, string reason = "cache-during")
        {
            if (element == null) return;
            var dispatcher = element.Dispatcher;
            if (dispatcher == null) return;

            if (host != null) MotionAssist.AttachMotionCacheOn(element, host, hostPx, reason);
            else if (MotionAssist.AttachMotionCache(element, reason) == null) return;   // 谁都没挂上：没有要摘的东西

            if (_cacheTimers.TryGetValue(element, out var previous))
            {
                try { previous.Stop(); } catch { }
                _cacheTimers.Remove(element);
            }

            System.Windows.Threading.DispatcherTimer? timer = null;
            timer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(Math.Max(1.0, animationMs) + 40.0),
                System.Windows.Threading.DispatcherPriority.Background,
                (_, __) =>
                {
                    if (timer != null)
                    {
                        try { timer.Stop(); } catch { }
                        if (_cacheTimers.TryGetValue(element, out var current) && ReferenceEquals(current, timer))
                            _cacheTimers.Remove(element);
                    }
                    // 摘缓存：根与"下沉后的内层容器"一起摘（映射在 MotionAssist 里维护）
                    MotionAssist.DetachMotionCache(element);
                },
                dispatcher);
            _cacheTimers[element] = timer;
            timer.Start();
        }

        private static void CancelCacheTimer(FrameworkElement element)
        {
            if (_cacheTimers.TryGetValue(element, out var timer))
            {
                try { timer.Stop(); } catch { }
                _cacheTimers.Remove(element);
            }
        }

        /// <summary>错峰限流上限：只让前 10 项参与 40ms 步长的错峰（任务 §2）。</summary>
        private const int MaxStaggerItems = 10;

        /// <summary><see cref="MaxStaggerItems"/> 的只读暴露（日志 / 实测核对用）。</summary>
        public static int MaxStaggerItemCount => MaxStaggerItems;

        #region ScaleHost（r3：scale 的实际作用对象）

        /// <summary>
        /// 「动效元素 → 真正被缩放的元素」。页根 / 内容根超过 1.2MP 时，scale 被下沉到
        /// 内层内容容器（见 <see cref="MotionAssist.ResolveScaleHost"/>），归位时也必须去动那个容器，
        /// 否则被中断的页根看着是 1.0、内层容器却永远卡在 0.95 的缩小态。
        /// 只在动效期间有值；归位（SettleElement / SettleOpacity / FinalizeElement）后即移除。
        /// </summary>
        private static readonly Dictionary<FrameworkElement, FrameworkElement> _scaleHosts =
            new Dictionary<FrameworkElement, FrameworkElement>();

        private static void RememberScaleHost(FrameworkElement root, FrameworkElement? host)
        {
            if (root == null) return;
            if (host == null || ReferenceEquals(host, root)) _scaleHosts.Remove(root);
            else _scaleHosts[root] = host;
        }

        private static FrameworkElement? TakeScaleHost(FrameworkElement root)
        {
            if (root == null) return null;
            if (!_scaleHosts.TryGetValue(root, out var host)) return null;
            _scaleHosts.Remove(root);
            return ReferenceEquals(host, root) ? null : host;
        }

        /// <summary>把下沉后的缩放目标归位（清时钟 + scale 写回 1.0）。</summary>
        private static void SettleScaleHost(FrameworkElement root)
        {
            var host = TakeScaleHost(root);
            if (host == null) return;
            try { SettleTransforms(host.RenderTransform); } catch { }
        }

        #endregion

        #region Settle（统一归位）

        /// <summary>
        /// <b>统一的归位入口</b>：所有播放入口在开始前、以及动画结束（Completed 或兜底定时器）时
        /// 都走这里，保证元素停回静止态：
        ///   * 停掉登记过的 Storyboard，清掉 RenderTransform / Opacity 上残留的动画时钟；
        ///   * TranslateTransform 归零（<c>(0,0)</c>），Opacity 归 1；
        ///   * 摘掉动画期挂上的 <see cref="BitmapCache"/>（长期挂缓存会让文字发虚）；
        ///   * 从"正在动效"集合里移除。
        /// 注意：只对动效系统自己碰过的元素调用，不要拿去复位 XAML 里本来就有
        /// 非 1 不透明度 / 非 0 位移的元素。
        /// </summary>
        public static void SettleElement(FrameworkElement? element, bool resetOpacity = true)
        {
            if (element == null) return;
            try
            {
                StopActive(element);
                CancelFinalizeTimer(element);
                CancelCacheTimer(element);

                if (resetOpacity)
                {
                    element.BeginAnimation(UIElement.OpacityProperty, null);
                    element.Opacity = 1.0;
                }

                SettleTransforms(element.RenderTransform);
                SettleScaleHost(element);                    // r3：内层容器的残留 scale 也一起归位
                MotionAssist.DetachMotionCache(element);     // r3：根 + 下沉后的内层容器一起摘缓存
                _motionTargets.Remove(element);
            }
            catch { }
        }

        /// <summary>
        /// 把所有"还在动效中"的元素立即归位 —— 新导航到来时先走这一步，
        /// 这样上一次没播完的动画不会把起始值（translateY=+30 / -6px）留在界面上。
        /// <paramref name="exceptSubtree"/> 用来排除"本次导航的新页面"自己那棵子树
        /// （新页的错峰卡片也登记在集合里，不能刚起动画就被归位）。
        /// </summary>
        public static void SettleActiveMotion(FrameworkElement? exceptSubtree = null)
        {
            foreach (var element in _motionTargets.ToArray())
            {
                if (exceptSubtree != null && ReferenceEquals(element, exceptSubtree)) continue;
                if (exceptSubtree != null && IsInSubtree(element, exceptSubtree)) continue;
                SettleElement(element);
            }
        }

        /// <summary>
        /// 把不透明度强制落到指定值（清时钟 + 写基值 + 摘掉动画期缓存 + 退出动效集合）。
        /// 侧栏折叠这种「淡出到 0 并一直保持 0」的场景不能走 <see cref="SettleElement"/>
        /// （那个会把 Opacity 复位成 1，收起后文字又会露出来）。
        /// </summary>
        public static void SettleOpacity(FrameworkElement? element, double opacity)
        {
            if (element == null) return;
            try
            {
                StopActive(element);
                CancelFinalizeTimer(element);
                CancelCacheTimer(element);
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = opacity;
                SettleScaleHost(element);                    // r3：内层容器的残留 scale 也一起归位
                MotionAssist.DetachMotionCache(element);     // r3：根 + 下沉后的内层容器一起摘缓存
                _motionTargets.Remove(element);
            }
            catch { }
        }

        public static int ActiveMotionCount => _motionTargets.Count;

        public static bool IsAnimating(FrameworkElement element) => element != null && _motionTargets.Contains(element);

        private static void RegisterTarget(FrameworkElement element)
        {
            if (element == null) return;
            if (!_motionTargets.Contains(element)) _motionTargets.Add(element);
        }

        private static bool IsInSubtree(DependencyObject? node, DependencyObject root)
        {
            var cur = node;
            int guard = 0;
            while (cur != null && guard++ < 128)
            {
                if (ReferenceEquals(cur, root)) return true;
                try { cur = VisualTreeHelper.GetParent(cur); } catch { return false; }
            }
            return false;
        }

        /// <summary>RenderTransform 归位：Translate 清零、Scale 的清掉卡住的时钟（回基值）。</summary>
        private static void SettleTransforms(Transform? transform)
        {
            switch (transform)
            {
                case null:
                    return;
                case TransformGroup group:
                    foreach (var child in group.Children) SettleTransforms(child);
                    return;
                case TranslateTransform translate:
                    translate.BeginAnimation(TranslateTransform.XProperty, null);
                    translate.BeginAnimation(TranslateTransform.YProperty, null);
                    translate.X = 0;
                    translate.Y = 0;
                    return;
                case ScaleTransform scale:
                    // 归位必须**写回 1.0**，不能只清时钟：
                    // PlayPageEnter / PlayContentFadeIn 会把基值先写成起点缩放（0.985 / 0.95），
                    // 只清时钟的话被中断的元素会永远卡在 0.985/0.95 那个「缩小态」。
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    scale.ScaleX = 1.0;
                    scale.ScaleY = 1.0;
                    return;
            }
        }

        /// <summary>只清时钟、不写值：让属性回到它自己的基值。</summary>
        private static void ClearClocks(Transform? transform)
        {
            switch (transform)
            {
                case null:
                    return;
                case TransformGroup group:
                    foreach (var child in group.Children) ClearClocks(child);
                    return;
                case TranslateTransform translate:
                    translate.BeginAnimation(TranslateTransform.XProperty, null);
                    translate.BeginAnimation(TranslateTransform.YProperty, null);
                    return;
                case ScaleTransform scale:
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    return;
                case RotateTransform rotate:
                    rotate.BeginAnimation(RotateTransform.AngleProperty, null);
                    return;
            }
        }

        /// <summary>把第一个 TranslateTransform 写成指定值（没有就什么都不做）。</summary>
        private static void ApplyTranslate(Transform? transform, double x, double y)
        {
            switch (transform)
            {
                case null:
                    return;
                case TransformGroup group:
                    foreach (var child in group.Children)
                    {
                        if (ContainsTranslate(child)) { ApplyTranslate(child, x, y); return; }
                    }
                    return;
                case TranslateTransform translate:
                    translate.X = x;
                    translate.Y = y;
                    return;
            }
        }

        private static bool ContainsTranslate(Transform? transform) => transform switch
        {
            TranslateTransform => true,
            TransformGroup group => group.Children.Any(ContainsTranslate),
            _ => false,
        };

        /// <summary>
        /// 动画的"落终态 + 清时钟 + 摘缓存"（不重置成静止态，因为离场动画的终态不是 0）。
        /// 所有播放入口都用它收尾。
        /// </summary>
        private static void FinalizeElement(FrameworkElement element, bool hasOpacity, double opacityTo, double xTo, double yTo,
            double? scaleTo = null)
        {
            var _sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                if (hasOpacity) element.Opacity = opacityTo;

                ClearClocks(element.RenderTransform);
                ApplyTranslate(element.RenderTransform, xTo, yTo);

                // r3：缩放目标可能是"下沉后的内层内容容器"——先清掉它的时钟，再按终态写回；
                // 根自己的 scale 时钟在上面 ClearClocks 里已经清掉（根不再参与 scale）。
                var scaleHost = TakeScaleHost(element);
                if (scaleHost != null)
                {
                    ClearClocks(scaleHost.RenderTransform);
                    ApplyScale(scaleHost.RenderTransform, scaleTo ?? 1.0);
                }
                else if (scaleTo.HasValue)
                {
                    ApplyScale(element.RenderTransform, scaleTo.Value);
                }

                MotionAssist.DetachMotionCache(element);
                CancelFinalizeTimer(element);
                _motionTargets.Remove(element);
            }
            catch { }
            _sw.Stop();
            if (_sw.Elapsed.TotalMilliseconds > 4.0)
                MotionPerf.Note("finalize-slow " + MotionAssist.DescribeElement(element), _sw.Elapsed.TotalMilliseconds);
        }

        /// <summary>把第一个 ScaleTransform 写成指定值（没有就什么都不做）。</summary>
        private static void ApplyScale(Transform? transform, double value)
        {
            switch (transform)
            {
                case null:
                    return;
                case TransformGroup group:
                    foreach (var child in group.Children)
                    {
                        if (ContainsScale(child)) { ApplyScale(child, value); return; }
                    }
                    return;
                case ScaleTransform scale:
                    scale.ScaleX = value;
                    scale.ScaleY = value;
                    return;
            }
        }

        private static bool ContainsScale(Transform? transform) => transform switch
        {
            ScaleTransform => true,
            TransformGroup group => group.Children.Any(ContainsScale),
            _ => false,
        };

        /// <summary>兜底：<paramref name="ms"/> 之后若还没落终态，就强制落一次（Completed 没来也不怕）。</summary>
        private static void SettleAfter(FrameworkElement element, double ms, Action action)
        {
            var dispatcher = element?.Dispatcher;
            if (dispatcher == null || action == null) return;

            CancelFinalizeTimer(element!);
            System.Windows.Threading.DispatcherTimer? timer = null;
            timer = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(Math.Max(1.0, ms) + 40.0),
                System.Windows.Threading.DispatcherPriority.Background,
                (_, __) =>
                {
                    if (timer != null)
                    {
                        try { timer.Stop(); } catch { }
                        if (_finalizeTimers.TryGetValue(element!, out var current) && ReferenceEquals(current, timer))
                            _finalizeTimers.Remove(element!);
                    }
                    try { action(); } catch { }
                },
                dispatcher);
            _finalizeTimers[element!] = timer;
            timer.Start();
        }

        private static void CancelFinalizeTimer(FrameworkElement element)
        {
            if (_finalizeTimers.TryGetValue(element, out var timer))
            {
                try { timer.Stop(); } catch { }
                _finalizeTimers.Remove(element);
            }
        }

        #endregion

        private static void StopActive(FrameworkElement target)
        {
            if (_activeStoryboards.TryGetValue(target, out var old))
            {
                try { old.Stop(); } catch { }
                _activeStoryboards.Remove(target);
            }
        }

        private static void Begin(Storyboard sb, FrameworkElement target, Action? onCompleted)
        {
            sb.Completed += (s, e) =>
            {
                // 只注销"自己这条"：被 Stop 掉的旧 Storyboard 若仍把 Completed 送过来，
                // 不能顺手把新一轮登记的那条从表里删掉（否则新动画再也 Stop 不掉）。
                if (_activeStoryboards.TryGetValue(target, out var current) && ReferenceEquals(current, sb))
                    _activeStoryboards.Remove(target);
                onCompleted?.Invoke();
            };
            _activeStoryboards[target] = sb;
        }

        private static void AddFade(Storyboard sb, FrameworkElement target, double from, double to, double ms, IEasingFunction ease)
        {
            if (Math.Abs(to - from) < 0.0001) return;
            var anim = new DoubleAnimation(from, to, AxolotlMotion.Ms(ms)) { EasingFunction = ease };
            Storyboard.SetTarget(anim, target);
            Storyboard.SetTargetProperty(anim, new PropertyPath(UIElement.OpacityProperty));
            sb.Children.Add(anim);
        }

        // ══════════════════════════════════════════════════════════════════════════════
        // ⚠⚠ 变换动画（Scale / Translate）**绝不能**走 Storyboard.SetTarget(anim, transform)。
        //     transform 是 Freezable，Storyboard 对它**完全不生效** —— 只有 opacity 会动。
        //     这正是 HANDOFF §6 第 1 条坑，而且它一直在生效：
        //       * PlayPopup 的 offsetY / fromScale 从来没动过 —— 元素一开始就停在偏移位置，
        //         直到 FinalizeElement 把终值写回去，于是**动画结束时整个元素跳一下**
        //         （实验室工具浮层实测：抓屏逐帧像素差在收尾处出现一次 56px 位移的巨跳，
        //           两次独立运行的 diff 数值完全相同 = 同一个固定量位移）；
        //       * 导航按钮入场的 scale 0.5 / translateX -32 同样是失效的。
        //     正确做法：直接对 Transform 调 BeginAnimation。
        //     完成时机不受影响 —— 所有调用点给 fade / scale / translate 传的都是**同一个时长**，
        //     Storyboard 里只剩 opacity 一条，Completed 仍在正确时刻触发；
        //     而三个 Add* 在 |to-from| < 0.0001 时会提前 return，所以「Storyboard 为空」
        //     等价于「一条动画都没起」，此时调用方立刻 Finish 也是对的。
        // ══════════════════════════════════════════════════════════════════════════════
        private static void AddScale(Storyboard sb, ScaleTransform scale, double from, double to, double ms, IEasingFunction ease)
        {
            if (Math.Abs(to - from) < 0.0001) return;
            var anim = new DoubleAnimation(from, to, AxolotlMotion.Ms(ms)) { EasingFunction = ease };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        private static void AddTranslateY(Storyboard sb, TranslateTransform t, double from, double to, double ms, IEasingFunction ease)
        {
            if (Math.Abs(to - from) < 0.0001) return;
            var anim = new DoubleAnimation(from, to, AxolotlMotion.Ms(ms)) { EasingFunction = ease };
            t.BeginAnimation(TranslateTransform.YProperty, anim);
        }

        private static void AddTranslateX(Storyboard sb, TranslateTransform t, double from, double to, double ms, IEasingFunction ease)
        {
            if (Math.Abs(to - from) < 0.0001) return;
            var anim = new DoubleAnimation(from, to, AxolotlMotion.Ms(ms)) { EasingFunction = ease };
            t.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private static void Reset(FrameworkElement target, ScaleTransform scale, TranslateTransform translate)
        {
            target.BeginAnimation(UIElement.OpacityProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.BeginAnimation(TranslateTransform.YProperty, null);
        }

        /// <summary>
        /// 唯一的动画实现：可选淡入淡出 + 可选 X/Y 位移 + 可选缩放。
        /// 结束时把属性落到终态并清掉动画时钟，避免留下 HoldEnd 的残留值。
        ///
        /// <para><b>r3</b>：缩放只作用在 <paramref name="scaleHost"/>（内层内容容器，面积 ≤ 1.2MP）上；
        /// 传 null 就是"这一处不做 scale"。<paramref name="target"/> 只承担 opacity 与位移。</para>
        /// </summary>
        private static void PlayMotion(
            FrameworkElement target,
            bool animateOpacity, double opacityFrom, double opacityTo,
            double xFrom, double xTo, double yFrom, double yTo,
            Duration duration, IEasingFunction ease, Action? onCompleted,
            TimeSpan? beginTime = null,
            Duration? slideDuration = null,
            IEasingFunction? slideEase = null,
            double? scaleFrom = null, double? scaleTo = null,
            Duration? scaleDuration = null, IEasingFunction? scaleEase = null,
            FrameworkElement? scaleHost = null)
        {
            var translate = MotionVisuals.EnsureTranslate(target);

            target.BeginAnimation(UIElement.OpacityProperty, null);
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.BeginAnimation(TranslateTransform.YProperty, null);

            if (animateOpacity) target.Opacity = opacityFrom;
            translate.X = xFrom;
            translate.Y = yFrom;

            // 可选缩放段（只动 ScaleTransform，属于 RenderTransform，不触发布局）。
            // ★ r3：**只在显式给出的 scaleHost 上做**，绝不对 target 本身（页面根 / 内容根）做 scale。
            //   整页根实测 1,281,280 设备像素：对它逐帧 scale 会让渲染线程每帧重新栅格化整棵子树，
            //   这就是"切换那一帧掉帧"的成本；scaleHost 是面积 ≤ 1.2MP 的内层内容容器，
            //   而且同一个容器上挂了 BitmapCache，缩放退化成对一张位图做重采样。
            //   调用方传 scaleHost: null = 这一处不做 scale（只保留 opacity + translateY）。
            ScaleTransform? scale = null;
            if (scaleFrom.HasValue && scaleTo.HasValue && scaleHost != null &&
                Math.Abs(scaleTo.Value - scaleFrom.Value) > 0.0001)
            {
                scale = MotionVisuals.EnsureScale(scaleHost);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = scale.ScaleY = scaleFrom.Value;
                RememberScaleHost(target, scaleHost);
            }

            var moveDuration = slideDuration ?? duration;
            var moveEase = slideEase ?? ease;
            var zoomDuration = scaleDuration ?? duration;
            var zoomEase = scaleEase ?? ease;

            // ═══════════════════════════════════════════════════════════════════════
            // 【根因修复】位移 / 缩放**不能再走 Storyboard.SetTarget**。
            //
            // 实测（[MotionPerf] page-enter sample 埋点）：把 DoubleAnimation 放进 Storyboard、
            // 用 Storyboard.SetTarget(anim, translate/scale) 指向 Transform（Freezable）时，
            // 动画**完全不生效** —— 采样显示 translateY 在整个 280ms 里一直钉在起始基值 56.0dip、
            // scaleX 一直钉在 0.985，只有 Opacity（目标是被动画元素本身）真的在动；
            // 动画结束那一刻 FinalizeElement 把基值写成 0/1，于是画面上只剩「淡入 + 突然跳一下」。
            // 这就是「接上了动画却看不出过渡」的真正原因，30px 时代就存在。
            //
            // 改成对 Transform 直接 BeginAnimation —— 与左导航胶囊（NavSliderTranslate）
            // 用了很久、逐帧验证过的那条路径完全一致。
            // ═══════════════════════════════════════════════════════════════════════
            var sb = new Storyboard();

            if (animateOpacity && Math.Abs(opacityTo - opacityFrom) > 0.0001)
            {
                var fade = new DoubleAnimation(opacityFrom, opacityTo, duration) { EasingFunction = ease };
                if (beginTime.HasValue) fade.BeginTime = beginTime;
                Storyboard.SetTarget(fade, target);
                Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
                sb.Children.Add(fade);
            }

            if (Math.Abs(xTo - xFrom) > 0.0001)
            {
                var slideX = new DoubleAnimation(xFrom, xTo, moveDuration) { EasingFunction = moveEase };
                if (beginTime.HasValue) slideX.BeginTime = beginTime;
                translate.BeginAnimation(TranslateTransform.XProperty, slideX);
            }

            if (Math.Abs(yTo - yFrom) > 0.0001)
            {
                var slideY = new DoubleAnimation(yFrom, yTo, moveDuration) { EasingFunction = moveEase };
                if (beginTime.HasValue) slideY.BeginTime = beginTime;
                translate.BeginAnimation(TranslateTransform.YProperty, slideY);
            }

            if (scale != null)
            {
                foreach (var prop in new[] { ScaleTransform.ScaleXProperty, ScaleTransform.ScaleYProperty })
                {
                    var zoom = new DoubleAnimation(scaleFrom!.Value, scaleTo!.Value, zoomDuration)
                    { EasingFunction = zoomEase };
                    if (beginTime.HasValue) zoom.BeginTime = beginTime;
                    scale.BeginAnimation(prop, zoom);
                }
            }

            bool hasMotion = Math.Abs(xTo - xFrom) > 0.0001 || Math.Abs(yTo - yFrom) > 0.0001 || scale != null;
            if (sb.Children.Count == 0 && !hasMotion)
            {
                onCompleted?.Invoke();
                return;
            }

            RegisterTarget(target);
            Begin(sb, target, null);
            if (sb.Children.Count > 0) sb.Begin(target);

            // 收尾（落终态 + 清时钟 + 摘缓存 + 回调）统一交给一次性定时器：
            // 时长取「淡入 / 位移 / 缩放」三者最大值，所以不会像以前那样在淡入结束时
            // 就提前归位、把还在跑的位移硬拽回去。
            Duration longest = duration;
            if (moveDuration.HasTimeSpan && longest.HasTimeSpan && moveDuration.TimeSpan > longest.TimeSpan)
                longest = moveDuration;
            if (zoomDuration.HasTimeSpan && longest.HasTimeSpan && zoomDuration.TimeSpan > longest.TimeSpan)
                longest = zoomDuration;
            double totalMs = (longest.HasTimeSpan ? longest.TimeSpan.TotalMilliseconds : 0.0)
                             + (beginTime?.TotalMilliseconds ?? 0.0);
            SettleAfter(target, totalMs, () =>
            {
                // 结束强制归位：清时钟 + 落到终态 + 摘掉动画期缓存（离场方向终态不是 0，
                // 所以走 FinalizeElement 而不是 SettleElement）。
                FinalizeElement(target, animateOpacity, opacityTo, xTo, yTo, scaleTo);
                onCompleted?.Invoke();
            });
        }

        /// <summary>「减少动画」时直接落到终态：清掉动画时钟并写入最终值。</summary>
        private static void ApplyRestingState(FrameworkElement element, double? opacity, double x, double y)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            if (opacity.HasValue) element.Opacity = opacity.Value;

            if (element.RenderTransform is TransformGroup group)
            {
                foreach (var child in group.Children)
                {
                    if (child is TranslateTransform t)
                    {
                        t.BeginAnimation(TranslateTransform.XProperty, null);
                        t.BeginAnimation(TranslateTransform.YProperty, null);
                        t.X = x;
                        t.Y = y;
                    }
                }
            }
            else if (element.RenderTransform is TranslateTransform translate)
            {
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.X = x;
                translate.Y = y;
            }
        }

        #endregion
    }

    /// <summary>
    /// <see cref="ColumnDefinition.WidthProperty"/> 的动画（WPF 没有内置的 GridLength 动画）。
    /// 用于右栏宽度 320ms cubic-bezier(0.22, 1, 0.36, 1) 的平滑过渡。
    ///
    /// ⚠️ From / To / EasingFunction <b>必须是依赖属性</b>，不能是普通 CLR 属性。
    /// <see cref="Animatable.BeginAnimation(DependencyProperty, AnimationTimeline)"/> 在应用动画时
    /// 会克隆 timeline，而 <see cref="Freezable"/> 的克隆（CloneCore）<b>只复制依赖属性</b> ——
    /// 普通 CLR 属性在克隆体上会退回字段默认值。历史上这里就是普通属性，导致
    /// GetCurrentValue 永远拿到 From=To=GridLength(0)，宽度动画恒输出 0：
    /// 收起表现为「瞬间归零」，展开表现为「原地卡住整个 320ms 再突然弹出」。
    /// </summary>
    public sealed class GridLengthAnimation : AnimationTimeline
    {
        public override Type TargetPropertyType => typeof(GridLength);

        protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

        public static readonly DependencyProperty FromProperty =
            DependencyProperty.Register(
                nameof(From), typeof(GridLength), typeof(GridLengthAnimation),
                new PropertyMetadata(new GridLength(0)));

        public static readonly DependencyProperty ToProperty =
            DependencyProperty.Register(
                nameof(To), typeof(GridLength), typeof(GridLengthAnimation),
                new PropertyMetadata(new GridLength(0)));

        public static readonly DependencyProperty EasingFunctionProperty =
            DependencyProperty.Register(
                nameof(EasingFunction), typeof(IEasingFunction), typeof(GridLengthAnimation),
                new PropertyMetadata(null));

        public GridLength From
        {
            get => (GridLength)GetValue(FromProperty);
            set => SetValue(FromProperty, value);
        }

        public GridLength To
        {
            get => (GridLength)GetValue(ToProperty);
            set => SetValue(ToProperty, value);
        }

        public IEasingFunction? EasingFunction
        {
            get => (IEasingFunction?)GetValue(EasingFunctionProperty);
            set => SetValue(EasingFunctionProperty, value);
        }

        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock clock)
        {
            GridLength from = From;
            GridLength to = To;

            double f = from.IsAbsolute ? from.Value : 0.0;
            double t = to.IsAbsolute ? to.Value : 0.0;

            double progress = clock.CurrentProgress ?? 0.0;
            IEasingFunction? ease = EasingFunction;
            if (ease != null) progress = ease.Ease(progress);

            return new GridLength(f + (t - f) * progress, GridUnitType.Pixel);
        }
    }
}
