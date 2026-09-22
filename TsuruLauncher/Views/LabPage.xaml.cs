using System;
using TsuruLauncher.Controls;
using TsuruLauncher.Services.Animation;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Newtonsoft.Json.Linq;

namespace TsuruLauncher.Views
{
    /// <summary>实验室里的一张工具卡（名称 / 说明 / 分类 / 收藏状态）。</summary>
    public sealed class LabTool : ObservableObject
    {
        public LabTool(string id, string name, string glyph, string description, string category, string hint)
        {
            Id = id;
            Name = name;
            Glyph = glyph;
            Description = description;
            Category = category;
            Hint = hint;
        }

        /// <summary>内部标识，用来决定「进入」时展开哪一块功能面板。</summary>
        public string Id { get; }

        public string Name { get; }

        /// <summary>卡片缩略图上的 emoji 符号（没有图片时用强调色渐变块 + 符号代替）。</summary>
        public string Glyph { get; }

        /// <summary>一句话说明。</summary>
        public string Description { get; }

        /// <summary>分类标签：创作 / 世界 / 工具。</summary>
        public string Category { get; }

        /// <summary>详情页里的一句补充提示。</summary>
        public string Hint { get; }

        private bool _isFavorite;

        /// <summary>收藏（星标）状态，只存在内存里。</summary>
        public bool IsFavorite
        {
            get => _isFavorite;
            set => SetProperty(ref _isFavorite, value);
        }
    }

    /// <summary>
    /// 实验室：离线小工具集合（崩溃日志分析 / JVM 参数 / 渐变文字 / 颜色 / 种子 / 文本编码）。
    /// 列表用卡片呈现，搜索、分类与常用筛选、收藏星标都在本地生效。
    /// </summary>
    public partial class LabPage : Page
    {
        private const string CategoryAll = "全部分类";
        private const string FilterFavorite = "常用";

        private bool _ready;

        /// <summary>工具目录。static 保存，所以离开页面再回来时收藏状态仍在（仅内存）。</summary>
        private static readonly List<LabTool> Tools = new List<LabTool>
        {
            new LabTool("crash", "崩溃日志分析", "\U0001F50D",
                "粘贴 crash-report 或 logs/latest.log，自动匹配常见崩溃特征并给出处理建议",
                "工具", "覆盖内存不足、模组冲突、Java 版本、显卡驱动等十余类常见问题。"),

            new LabTool("jvm", "JVM 参数生成器", "\u2699\uFE0F",
                "按内存大小生成推荐的 GC 参数，可直接粘到「设置 → 高级 → JVM 参数」",
                "工具", "内置 Aikar 优化参数，也可以切到 ZGC（需要 Java 17+）。"),

            new LabTool("gradient", "渐变色文字生成器", "\U0001F3A8",
                "做启动器横幅、服务器名字时用的渐变文字，实时预览",
                "创作", "一键复制 CSS 或 XAML，粘贴到网页 / 界面里即可。"),

            new LabTool("color", "颜色转换", "\U0001F9EA",
                "HEX / RGB / HSL 互转，改任意一栏其它栏都会同步",
                "创作", "取到满意的颜色后直接复制 HEX 就能用。"),

            new LabTool("seed", "种子工具", "\U0001F331",
                "把文字种子换算成游戏实际使用的数字种子",
                "世界", "用的是 Java String.hashCode()，与游戏内的换算结果一致。"),

            new LabTool("text", "文本 / 编码工具", "\U0001F524",
                "Base64、URL 编解码与 JSON 美化，全部离线处理",
                "工具", "长文本也不会卡，输出框可以直接全选复制。")
        };

        // 工具输入框的默认值：属于工具数据（用户可随意改），不是界面配色。
        private const string DefaultGradientStart = "#4ADE80";
        private const string DefaultGradientEnd = "#38BDF8";

        // Aikar 推荐的 G1 参数（公开的社区常用配置）
        private const string AikarFlags =
            "-XX:+AlwaysPreTouch -XX:+ParallelRefProcEnabled -XX:+DisableExplicitGC -XX:+UnlockExperimentalVMOptions " +
            "-XX:+UseG1GC -XX:G1NewSizePercent=30 -XX:G1MaxNewSizePercent=40 -XX:G1HeapRegionSize=8M -XX:G1ReservePercent=20 " +
            "-XX:G1HeapWastePercent=5 -XX:G1MixedGCCountTarget=4 -XX:InitiatingHeapOccupancyPercent=15 " +
            "-XX:G1MixedGCLiveThresholdPercent=90 -XX:G1RSetUpdatingPauseTimePercent=5 -XX:SurvivorRatio=32 " +
            "-XX:+PerfDisableSharedMem -XX:MaxTenuringThreshold=1 -Dusing.aikars.flags=https://mcflags.emc.gs -Daikars.new.flags=true";

        public LabPage()
        {
            InitializeComponent();

            // 渐变 / 颜色工具的默认输入值（数据，不是主题配色）
            GradientColor1.Text = DefaultGradientStart;
            GradientColor2.Text = DefaultGradientEnd;
            ColorHexBox.Text = DefaultGradientStart;
            Color? seedColor = ParseColor(DefaultGradientStart);
            if (seedColor != null)
            {
                ColorRBox.Text = seedColor.Value.R.ToString(CultureInfo.InvariantCulture);
                ColorGBox.Text = seedColor.Value.G.ToString(CultureInfo.InvariantCulture);
                ColorBBox.Text = seedColor.Value.B.ToString(CultureInfo.InvariantCulture);
            }

            _ready = true;

            UpdateGradient();
            UpdateColor();
            UpdateJvmArgs();
            UpdateSeed();
            RefreshToolList();

            Utilities.Logger.LogDebug("[Lab] page ready");
        }

        // ─────────────── 工具列表：搜索 / 筛选 / 收藏 / 进入 ───────────────

        private void ToolSearch_Changed(object sender, TextChangedEventArgs e) => RefreshToolList();

        private void Filter_Changed(object sender, SelectionChangedEventArgs e) => RefreshToolList();

        /// <summary>按关键词 + 分类 + 常用重新生成卡片列表。</summary>
        private void RefreshToolList()
        {
            if (!_ready) return;

            string query = (ToolSearchBox.Text ?? string.Empty).Trim();
            string category = (CategoryFilter.SelectedItem as ComboBoxItem)?.Content as string ?? CategoryAll;
            bool favoritesOnly = string.Equals((FavoritesFilter.SelectedItem as ComboBoxItem)?.Content as string,
                                               FilterFavorite, StringComparison.Ordinal);

            var list = Tools.Where(tool =>
                    (category.Length == 0 || category == CategoryAll || tool.Category == category) &&
                    (!favoritesOnly || tool.IsFavorite) &&
                    (query.Length == 0 ||
                     tool.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     tool.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                     tool.Category.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            ToolList.ItemsSource = list;
            ToolCountText.Text = list.Count == Tools.Count
                ? Tools.Count + " 个工具"
                : list.Count + " / " + Tools.Count + " 个工具";
            EmptyState.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Favorite_Click(object sender, RoutedEventArgs e)
        {
            // 在「常用」筛选下取消收藏，卡片需要马上移出列表；
            // 延迟到本次点击事件处理完之后再刷新，避免在点击中改动 ItemsSource。
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshToolList));
        }

        private void EnterTool_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element && element.DataContext is LabTool tool)
                OpenTool(tool);
        }

        /// <summary>
        /// 「进入 / 返回」的终态兜底定时器。
        ///
        /// 为什么必须有：覆盖层 / 面板的终态（Opacity / Scale / Translate）以及
        /// <c>ToolDetailOverlay.Visibility</c> 都挂在 <c>Storyboard.Completed</c> 回调里。
        /// 只要那次回调因为任何原因没来（动画时钟没被推进、被下一轮播放接管、
        /// 中途抛异常、调用方自己被换页打断），覆盖层就会**永远停在中间不透明度**，
        /// 而且因为 Visibility 还是 Visible，它会一直盖在工具列表上吞掉所有点击 ——
        /// 用户看到的就是「进工具后覆盖层停在半透明状态、整页发白、点什么都没反应」。
        ///
        /// 这里在动画时长之外再等一小段，检查覆盖层是不是真的落到了期望终态；
        /// 没落到就用 SettleElement / SettleOpacity 强制落地（清时钟 + 写终值 + 摘缓存）。
        /// 只检查「该开没开 / 该收没收」这类致命中间态，正常动画不会被它打扰。
        /// </summary>
        private DispatcherTimer? _toolOverlayWatchdog;

        /// <summary>进入 / 返回动画结束后，强制把覆盖层与列表落到确定的终态。</summary>
        private void ForceToolOverlayState(bool open)
        {
            try
            {
                if (open)
                {
                    ToolDetailOverlay.Visibility = Visibility.Visible;
                    PageTransition.SettleElement(ToolDetailOverlay);   // Opacity=1 + 变换归零 + 摘缓存 + 清时钟
                    PageTransition.SettleElement(ToolDetailPanel);
                    if (ListViewScroll != null) PageTransition.SettleOpacity(ListViewScroll, 0.0);
                }
                else
                {
                    ToolDetailOverlay.Visibility = Visibility.Hidden;
                    PageTransition.SettleOpacity(ToolDetailOverlay, 0.0);
                    PageTransition.SettleElement(ToolDetailPanel);
                    if (ListViewScroll != null) PageTransition.SettleOpacity(ListViewScroll, 1.0);
                }
                Utilities.Logger.LogInfo("[MotionPerf] lab-tool-overlay force-fix open=" + open +
                    " overlay.Visibility=" + ToolDetailOverlay.Visibility +
                    " overlay.Opacity=" + ToolDetailOverlay.Opacity.ToString("0.###") +
                    " list.Opacity=" + (ListViewScroll == null ? -1 : ListViewScroll.Opacity).ToString("0.###"));
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "LabPage.ForceToolOverlayState");
            }
        }

        /// <summary>
        /// 武装兜底：<paramref name="expectOpen"/> = 这次点击期望的终态（进入 true / 返回 false）。
        /// 之后的 <see cref="AxolotlMotion.FloatingPanelLeaveMs"/> + 120ms 检查一次，
        /// 没落到期望终态就强制落地。连点时只保留最后一个定时器。
        /// </summary>
        private void ArmToolOverlayWatchdog(bool expectOpen)
        {
            // TSURU_MOTION_WATCHDOG=0 = 改前的行为（只靠动画回调），用于 A/B 对照。
            if (!PageTransition.FinalizeWatchdogEnabled) return;
            try
            {
                _toolOverlayWatchdog?.Stop();
                DispatcherTimer? timer = null;
                timer = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(AxolotlMotion.FloatingPanelLeaveMs + 120.0),
                    DispatcherPriority.Background,
                    (_, __) =>
                    {
                        try { timer?.Stop(); } catch { }
                        if (!ReferenceEquals(_toolOverlayWatchdog, timer)) return;
                        _toolOverlayWatchdog = null;
                        MotionPerf.Mark("overlay-watchdog");

                        bool visible = ToolDetailOverlay.Visibility == Visibility.Visible;
                        bool opaque = ToolDetailOverlay.Opacity > 0.999;
                        bool settled = Math.Abs(ToolDetailOverlay.Opacity - (expectOpen ? 1.0 : 0.0)) < 0.002;

                        // 期望「打开」：必须是 Visible 且已经完全不透明（半透明 = 停在中间态）。
                        // 期望「收起」：必须已经 Collapsed（还 Visible 就是那次 Completed 没来）。
                        bool ok = expectOpen ? (visible && opaque) : !visible;
                        if (!ok || !settled) ForceToolOverlayState(expectOpen);
                    },
                    Dispatcher);
                _toolOverlayWatchdog = timer;
                timer.Start();
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "LabPage.ArmToolOverlayWatchdog");
            }
        }

        /// <summary>打开工具：用页内覆盖层显示原来的功能面板。</summary>
        private void OpenTool(LabTool tool)
        {
            try
            {
            DetailGlyph.Text = tool.Glyph;
            DetailTitle.Text = tool.Name;
            DetailCategoryText.Text = tool.Category;
            DetailDescription.Text = tool.Description;
            DetailHintText.Text = tool.Hint;
            DetailFavorite.DataContext = tool;

            PanelCrash.Visibility = tool.Id == "crash" ? Visibility.Visible : Visibility.Hidden;
            PanelJvm.Visibility = tool.Id == "jvm" ? Visibility.Visible : Visibility.Hidden;
            PanelGradient.Visibility = tool.Id == "gradient" ? Visibility.Visible : Visibility.Hidden;
            PanelColor.Visibility = tool.Id == "color" ? Visibility.Visible : Visibility.Hidden;
            PanelSeed.Visibility = tool.Id == "seed" ? Visibility.Visible : Visibility.Hidden;
            PanelText.Visibility = tool.Id == "text" ? Visibility.Visible : Visibility.Hidden;

            // 帧间隔采样必须**从点击这一刻**就装（TSURU_FRAME_DBG=1 才开）。
            // 之前是装在延迟回调里（布局之后），结果把最贵的那一帧——覆盖层刚 Visible
            // 时 WPF 冷布局整棵子树的那一帧——整个漏掉了，测出来的数字自然是「挺顺的」。
            MotionPerf.ProbeFrames("lab-enter tool=" + tool.Id, 700);

            // 进入 = 遮罩淡入 200ms + 面板 opacity 0->1 / scale 0.94->1 + translateY 20->0（220ms 过冲），
            // 列表侧 120ms ease 淡出（page-slide-leave）。
            //
            //   直接在当前帧起播 —— 不要再往后推一帧。
            //   （中途版本曾把播放推迟到 DispatcherPriority.Loaded，那是为了让 CacheDuring
            //     能在布局之后挂上 BitmapCache；现在这两个元素已经用
            //     motion:MotionAssist.CacheDisabled 彻底关掉缓存，延迟播放就只剩
            //     「点击后多等一帧」这一个副作用了。）
            ToolDetailOverlay.Visibility = Visibility.Visible;
            ToolDetailOverlay.Opacity = 0.0;
            ToolDetailPanel.Opacity = 0.0;
            PlayToolListFade(show: false);

            // ── 完全照搬 PlayPageEnter（页面切换是公认流畅的路径）─────────────────────
            // 之前这里是「另一套写法」，和页面进场有三处结构性差异，逐条对齐：
            //   ① 整页元素**不做 scale**。PlayPageEnter 的注释写得很明确：
            //      「页根本身绝不 scale（实测 1,281,280 设备像素）」—— 整页逐帧 scale
            //      等于渲染线程每帧按新比例重新栅格化整棵子树。面板 1,155,592 设备像素，同理。
            //   ② 曲线与位移直接取页面进场那一组常量：
            //      PageEnterSlidePx = 56px、PageEnterMoveEase = cubic-bezier(0.22,1,0.36,1)、
            //      PageEnterSlideMs = 280ms。不再自己发明 0.94 的 scale + 20px 位移 + 过冲曲线。
            //   ③ 面板不再单独播一份动画（页面进场也只动一个元素），只把终态写死。
            //   缓存仍不挂：实测栅格化一张 1.15M 设备像素的位图要 90~98ms，
            //   而它随后还要再拆一次 —— 两次栅格化比整个动画都贵（见 HANDOFF §9.2 第 8 条）。
            using (MotionPerf.Measure("lab-tool-enter", "tool=" + tool.Id))
            {
                // ⚠ 曲线不能直接用 PageEnterMoveEase：那条 cubic-bezier(0.22,1,0.36,1) 是**强前倾**的
                // （前 37% 时间走完 89% 的位移），PlayPageEnter 只把它用在**位移**上，
                // 透明度用的是平滑的 Ease。PlayPopup 只有一个 ease 参数，
                // 若把它同时用在透明度上，观感就是「190ms 内冲到 95%、之后 90ms 几乎不动」
                // —— 抓屏逐帧算像素差可以清楚看到「先猛动一下、然后冻结、再跳到终态」。
                // 所以这里透明度与位移统一用平滑的 Ease。
                PageTransition.PlayPopup(ToolDetailOverlay, show: true, fromScale: 1.0,
                    durationMs: AxolotlMotion.PageEnterSlideMs,
                    ease: AxolotlMotion.Ease,
                    offsetY: AxolotlMotion.PageEnterSlidePx,
                    useCache: false);

                // 面板：不参与动画，直接落终态（透明度 1、无位移、无缩放）。
                ToolDetailPanel.Opacity = 1.0;
            }

            // 滚动条归位放到 Background 优先级：它可能触发一次真实的滚动布局，
            // 落在动画起步那一帧里就是一次掉帧。动画只有 220ms，等它跑完再归位完全来得及。
            Dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(() => { try { DetailScroll?.ScrollToTop(); } catch { } MotionPerf.Mark("scrolltotop"); }));

            Utilities.Logger.LogInfo(
                $"[MotionPerf] lab enter tool={tool.Id} mask {AxolotlMotion.OverlayMaskMs}ms ease-out ; " +
                $"panel {AxolotlMotion.FloatingPanelMs}ms cubic-bezier(0.15,1.4,0.64,0.96) scale {AxolotlMotion.FloatingPanelFromScale}->1 " +
                $"translateY {AxolotlMotion.FloatingPanelOffsetPx}px->0 (was 125ms/scale 0.85/no-translate) " +
                $"; list fade-out {AxolotlMotion.PageLeaveMs}ms (animations={PageTransition.AnimationsEnabled})");

            ArmToolOverlayWatchdog(expectOpen: true);
            }
            catch (Exception ex)
            {
                // 进入过程中抛任何异常都不许把 UI 留在中间态：直接落到「工具面板已打开」的终态。
                Utilities.Logger.LogError(ex, "LabPage.OpenTool");
                ForceToolOverlayState(open: true);
            }
        }

        private void DetailFavorite_Click(object sender, RoutedEventArgs e) => RefreshToolList();

        private void BackToList_Click(object sender, RoutedEventArgs e)
        {
            try
            {
            // 返回列表 = 进入的反向，200ms（遮罩淡出 + 面板缩回 scale 0.94 / 淡出），
            // 动画结束后才 Collapsed —— 不是 Visibility 硬切；列表侧同时 180ms ease 淡入。
            using (MotionPerf.Measure("lab-tool-exit"))
            {
                // ⚠ 退出这一侧显式关缓存 + 先摘干净（第一道防线是 XAML 上的
                // motion:MotionAssist.CacheDisabled，这里是双保险）。
                // 这两个元素面积贴着 1.2MP 安全线（面板 1155592 设备像素），退出动画结束时
                // 覆盖层紧接着要 Visibility=Collapsed ——「缓存 + 布局变更」会让渲染线程崩：
                // UCEERR_RENDERTHREADFAILURE (0x88980406)，表现就是点「返回」整窗卡死
                // （HANDOFF §6.2 的同一个坑，触发元素换成了浮层内那块面板）。
                MotionAssist.DetachMotionCache(ToolDetailOverlay);
                MotionAssist.DetachMotionCache(ToolDetailPanel);

                PageTransition.PlayPopup(ToolDetailOverlay, show: false, fromScale: 1.0,
                    durationMs: AxolotlMotion.PageEnterSlideMs,
                    ease: AxolotlMotion.Ease,
                    offsetY: AxolotlMotion.PageEnterSlidePx,
                    onCompleted: () => ToolDetailOverlay.Visibility = Visibility.Hidden,
                    useCache: false);
                ToolDetailPanel.Opacity = 1.0;
                PlayToolListFade(show: true);
            }

            DetailFavorite.DataContext = null;
            RefreshToolList();

            Utilities.Logger.LogInfo(
                $"[Motion] lab back to list overlay/panel {AxolotlMotion.FloatingPanelLeaveMs}ms ease-in-out -> scale " +
                $"{AxolotlMotion.FloatingPanelFromScale} ; list fade-in {AxolotlMotion.PageEnterMs}ms " +
                $"(animations={PageTransition.AnimationsEnabled})");

            ArmToolOverlayWatchdog(expectOpen: false);
            MotionPerf.ProbeFrames("lab-exit", 400);
            }
            catch (Exception ex)
            {
                // 返回过程中抛任何异常都不许把覆盖层留在屏幕上（它会吞掉所有点击）：
                // 直接落到「已收起 + 列表可见」的终态。
                Utilities.Logger.LogError(ex, "LabPage.BackToList");
                ForceToolOverlayState(open: false);
            }
        }

        /// <summary>列表侧淡入 / 淡出（对应页面级 180ms 进入 / 120ms 离开那一组）。</summary>
        private void PlayToolListFade(bool show)
        {
            if (ListViewScroll == null) return;

            if (!PageTransition.AnimationsEnabled)
            {
                ListViewScroll.BeginAnimation(OpacityProperty, null);
                ListViewScroll.Opacity = show ? 1 : 0;
                return;
            }

            var ms = show ? AxolotlMotion.PageEnterMs : AxolotlMotion.PageLeaveMs;
            var ease = show ? AxolotlMotion.Ease : AxolotlMotion.EaseIn;
            double target = show ? 1.0 : 0.0;
            var anim = new System.Windows.Media.Animation.DoubleAnimation(target,
                AxolotlMotion.Ms(ms)) { EasingFunction = ease };
            ListViewScroll.BeginAnimation(OpacityProperty, anim);

            // 兜底：这条淡入/淡出**没有** Completed 回调（纯 BeginAnimation）。
            // 万一动画时钟没被推进，列表就会永远停在半透明上 ——「整页发白」的另一半。
            // 时长 + 80ms 之后如果还没到目标值，直接清时钟写终值。
            DispatcherTimer? guard = null;
            guard = new DispatcherTimer(
                TimeSpan.FromMilliseconds(ms + 80.0),
                DispatcherPriority.Background,
                (_, __) =>
                {
                    try { guard?.Stop(); } catch { }
                    MotionPerf.Mark("listfade-guard");
                    try
                    {
                        if (ListViewScroll != null && Math.Abs(ListViewScroll.Opacity - target) > 0.01)
                        {
                            PageTransition.SettleOpacity(ListViewScroll, target);
                            Utilities.Logger.LogInfo("[MotionPerf] lab-tool-list force-fix opacity=" + target.ToString("0.##"));
                        }
                    }
                    catch (Exception ex) { Utilities.Logger.LogError(ex, "LabPage.PlayToolListFade.guard"); }
                },
                Dispatcher);
            guard.Start();
        }

        // ─────────────── 崩溃日志分析 ───────────────
        private static readonly (string[] Keys, string Cause, string Advice)[] CrashRules =
        {
            (new[] { "java.lang.OutOfMemoryError", "OutOfMemoryError", "Java heap space" },
                "内存不足（Java 堆内存耗尽）", "在「设置 → 启动」里把最大内存调到 4096MB 以上；也检查 JVM 参数里 -Xmx 是否被写得太小。"),

            (new[] { "UnsupportedClassVersionError", "class file version" },
                "Java 版本与游戏 / 模组不匹配", "1.17+ 需要 Java 17，1.20.5+ 建议 Java 21。在「设置 → Java」里切换到对应版本，或用自动下载。"),

            (new[] { "NoSuchMethodError", "NoClassDefFoundError", "AbstractMethodError" },
                "模组与游戏版本 / 加载器版本不匹配", "检查模组的 MC 版本与加载器版本是否一致；旧模组在新版本上常报此类错误。"),

            (new[] { "Mixin apply failed", "MixinTransformerError", "mixin.injection.throwables", "Mixin apply" },
                "Mixin 注入冲突（两个模组改了同一处代码）", "用二分法禁用一半模组定位冲突者；常见冲突：优化模组（Sodium/Embeddium）、光影前置、界面模组。"),

            (new[] { "DuplicateModsFoundException", "Duplicate mods" },
                "存在重复模组", "在「版本设置 → 内容管理 → 模组」里删掉重复或用不到的版本，同名模组不要放两份。"),

            (new[] { "Missing or unsupported mandatory dependencies", "MissingDependency", "requires" },
                "缺少前置模组 / 前置版本不对", "按提示安装缺少的前置（API、Library 类模组），注意前置也要匹配游戏版本。"),

            (new[] { "Pixel format not accelerated", "GLFW error", "OpenGL", "No OpenGL context", "Failed to create window" },
                "显卡驱动 / OpenGL 问题", "更新显卡驱动；笔记本请确认游戏用的是独显；必要时删除 options.txt 里的全屏设置再试。"),

            (new[] { "Could not find or load main class", "Error: Could not find or load main class" },
                "版本文件损坏或主类缺失", "在「版本设置 → 维护」里重新下载该版本的 client jar / 依赖，或重新安装加载器。"),

            (new[] { "AccessDeniedException", "The process cannot access the file", "拒绝访问" },
                "文件被占用或没有写入权限", "关闭其它正在运行的启动器 / 游戏实例；把游戏目录移出受保护目录（如 Program Files），或关闭杀毒软件的文件锁定。"),

            (new[] { "Invalid or corrupt jarfile", "zip file is empty", "Corrupt" },
                "依赖文件下载不完整 / 损坏", "删除对应 libraries 下的文件后重新启动，让启动器重新校验下载；网络不稳定时建议开 BMCLAPI 镜像。"),

            (new[] { "authlib-injector", "yggdrasil", "Failed to login", "Invalid session" },
                "外置登录 / 会话校验失败", "重新登录账号；外置登录账号失败时先在「设置」里点「测试连接」确认皮肤站可用，并确认 authlib-injector 已下载。"),

            (new[] { "java.net.UnknownHostException", "Connection timed out", "SocketTimeoutException" },
                "网络连接失败", "无法连接验证 / 皮肤服务器，检查代理与 DNS；离线账号可先用离线模式进入游戏。"),

            (new[] { "Haven't finished loading", "Shaders", "iris", "OptiFine" },
                "光影相关错误", "光影需要 Iris / OptiFine 才能生效；确认光影包与加载器版本匹配，或先在游戏内关闭光影。")
        };

        private void AnalyzeCrash_Click(object sender, RoutedEventArgs e)
        {
            string text = CrashInput.Text ?? string.Empty;
            if (text.Trim().Length < 8)
            {
                CrashResultPanel.Visibility = Visibility.Visible;
                CrashVerdictText.Text = "内容太少，粘一段日志再分析吧。";
                CrashDetailList.ItemsSource = null;
                return;
            }

            var hits = new List<string>();
            foreach (var rule in CrashRules)
            {
                if (rule.Keys.Any(k => text.IndexOf(k, StringComparison.OrdinalIgnoreCase) >= 0))
                    hits.Add("【" + rule.Cause + "】\n" + rule.Advice);
            }

            // 常见 Java 崩溃行
            var exceptionLine = text.Split('\n')
                .FirstOrDefault(l => l.Contains("Exception in thread") || l.Contains("Caused by:") || l.Contains("FATAL"));

            CrashResultPanel.Visibility = Visibility.Visible;
            if (hits.Count == 0)
            {
                CrashVerdictText.Text = "没有匹配到内置的常见崩溃特征，可以先看看下面的关键行。";
                var fallback = new List<string>();
                if (!string.IsNullOrWhiteSpace(exceptionLine)) fallback.Add("关键行：\n" + exceptionLine.Trim());
                fallback.Add("建议：把这条日志发到模组作者或交流群；也可以先在「版本设置 → 内容管理 → 日志」里打开 latest.log 对照时间点。");
                CrashDetailList.ItemsSource = fallback;
                return;
            }

            CrashVerdictText.Text = "识别到 " + hits.Count + " 个可能原因：";
            if (!string.IsNullOrWhiteSpace(exceptionLine)) hits.Insert(0, "关键行：\n" + exceptionLine.Trim());
            CrashDetailList.ItemsSource = hits;
        }

        private void LoadCrashFile_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "日志文件 (*.log;*.txt)|*.log;*.txt|所有文件 (*.*)|*.*",
                Title = "选择崩溃日志"
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                string content = System.IO.File.ReadAllText(dialog.FileName);
                if (content.Length > 400000) content = content.Substring(content.Length - 400000);
                CrashInput.Text = content;
                AnalyzeCrash_Click(sender, e);
            }
            catch (Exception ex)
            {
                iOS26Dialog.Show("读取失败: " + ex.Message, "错误", DialogIcon.Error, DialogButtons.OK);
            }
        }

        private void ClearCrash_Click(object sender, RoutedEventArgs e)
        {
            CrashInput.Text = string.Empty;
            CrashResultPanel.Visibility = Visibility.Collapsed;
        }

        // ─────────────── JVM 参数 ───────────────
        private void JvmMemory_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateJvmArgs();
        private void JvmToggle_Changed(object sender, RoutedEventArgs e) => UpdateJvmArgs();

        private void UpdateJvmArgs()
        {
            if (!_ready) return;
            int mb = (int)Math.Round(JvmMemorySlider.Value);
            JvmMemoryText.Text = mb + " MB";

            var sb = new StringBuilder();
            sb.Append("-Xms").Append(Math.Max(512, mb / 4)).Append("M -Xmx").Append(mb).Append("M ");
            if (UseZgcCheck.IsChecked == true)
                sb.Append("-XX:+UseZGC -XX:+ZGenerational -XX:+AlwaysPreTouch ");
            else if (AikarCheck.IsChecked == true)
                sb.Append(AikarFlags);
            else
                sb.Append("-XX:+UseG1GC -XX:MaxGCPauseMillis=200 -XX:+UnlockExperimentalVMOptions -XX:G1NewSizePercent=20 -XX:G1ReservePercent=20");

            JvmArgsOutput.Text = sb.ToString().Trim();
        }

        private void CopyJvmArgs_Click(object sender, RoutedEventArgs e)
            => CopyToClipboard(JvmArgsOutput.Text, "JVM 参数");

        // ─────────────── 渐变文字 ───────────────
        private void GradientText_Changed(object sender, TextChangedEventArgs e) => UpdateGradient();
        private void GradientAngle_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateGradient();

        private static Color? ParseColor(string? value)
        {
            string hex = (value ?? string.Empty).Trim();
            if (hex.Length == 0) return null;
            if (!hex.StartsWith("#")) hex = "#" + hex;
            try { return (Color)ColorConverter.ConvertFromString(hex); }
            catch { return null; }
        }

        private void UpdateGradient()
        {
            if (!_ready) return;

            string text = string.IsNullOrEmpty(GradientTextBox.Text) ? "Tsuru Launcher" : GradientTextBox.Text;
            Color? c1 = ParseColor(GradientColor1.Text) ?? Color.FromRgb(0x4A, 0xDE, 0x80);
            Color? c2 = ParseColor(GradientColor2.Text) ?? Color.FromRgb(0x38, 0xBD, 0xF8);
            double angle = GradientAngle.Value;
            GradientAngleText.Text = ((int)angle) + "°";

            var brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
            brush.GradientStops.Add(new GradientStop(c1.Value, 0));
            brush.GradientStops.Add(new GradientStop(c2.Value, 1));

            double radians = angle * Math.PI / 180.0;
            brush.StartPoint = new Point(0.5 - Math.Cos(radians) / 2, 0.5 - Math.Sin(radians) / 2);
            brush.EndPoint = new Point(0.5 + Math.Cos(radians) / 2, 0.5 + Math.Sin(radians) / 2);

            GradientPreview.Text = text;
            GradientPreview.Foreground = brush;
        }

        private static string HexOf(Color c) => "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");

        private void CopyGradientCss_Click(object sender, RoutedEventArgs e)
        {
            Color c1 = ParseColor(GradientColor1.Text) ?? Colors.LimeGreen;
            Color c2 = ParseColor(GradientColor2.Text) ?? Colors.DeepSkyBlue;
            string css = "background: linear-gradient(" + ((int)GradientAngle.Value) + "deg, " + HexOf(c1) + ", " + HexOf(c2) + ");\n" +
                         "-webkit-background-clip: text;\n-webkit-text-fill-color: transparent;";
            CopyToClipboard(css, "CSS");
        }

        private void CopyGradientXaml_Click(object sender, RoutedEventArgs e)
        {
            Color c1 = ParseColor(GradientColor1.Text) ?? Colors.LimeGreen;
            Color c2 = ParseColor(GradientColor2.Text) ?? Colors.DeepSkyBlue;
            string xaml = "<TextBlock Text=\"" + GradientPreview.Text + "\" FontSize=\"26\" FontWeight=\"Bold\">\n" +
                          "  <TextBlock.Foreground>\n" +
                          "    <LinearGradientBrush StartPoint=\"0,0.5\" EndPoint=\"1,0.5\">\n" +
                          "      <GradientStop Color=\"" + HexOf(c1) + "\" Offset=\"0\"/>\n" +
                          "      <GradientStop Color=\"" + HexOf(c2) + "\" Offset=\"1\"/>\n" +
                          "    </LinearGradientBrush>\n" +
                          "  </TextBlock.Foreground>\n</TextBlock>";
            CopyToClipboard(xaml, "XAML");
        }

        // ─────────────── 颜色转换 ───────────────
        private void ColorHex_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            Color? color = ParseColor(ColorHexBox.Text);
            if (color == null) return;
            ColorRBox.Text = color.Value.R.ToString();
            ColorGBox.Text = color.Value.G.ToString();
            ColorBBox.Text = color.Value.B.ToString();
            ApplyColor(color.Value);
        }

        private void ColorRgb_Changed(object sender, TextChangedEventArgs e)
        {
            if (!_ready) return;
            if (!byte.TryParse(ColorRBox.Text, out byte r)) return;
            if (!byte.TryParse(ColorGBox.Text, out byte g)) return;
            if (!byte.TryParse(ColorBBox.Text, out byte b)) return;

            var color = Color.FromRgb(r, g, b);
            ColorHexBox.Text = HexOf(color);
            ApplyColor(color);
        }

        private void ApplyColor(Color color)
        {
            ColorSwatch.Background = new SolidColorBrush(color);
            ToHsl(color, out double h, out double s, out double l);
            ColorHslBox.Text = "HSL: " + Math.Round(h) + "°, " + Math.Round(s * 100) + "%, " + Math.Round(l * 100) + "%   |   RGB: " +
                               color.R + ", " + color.G + ", " + color.B + "   |   HEX: " + HexOf(color);
        }

        private void UpdateColor()
        {
            Color? color = ParseColor(ColorHexBox.Text);
            if (color != null) ApplyColor(color.Value);
        }

        private static void ToHsl(Color color, out double h, out double s, out double l)
        {
            double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
            double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2;
            double d = max - min;
            if (d == 0) { h = 0; s = 0; return; }

            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            if (max == r) h = ((g - b) / d + (g < b ? 6 : 0)) * 60;
            else if (max == g) h = ((b - r) / d + 2) * 60;
            else h = ((r - g) / d + 4) * 60;
        }

        private void CopyColor_Click(object sender, RoutedEventArgs e) => CopyToClipboard(ColorHexBox.Text, "颜色值");

        // ─────────────── 种子 ───────────────
        private void Seed_Changed(object sender, TextChangedEventArgs e) => UpdateSeed();

        private void UpdateSeed()
        {
            if (!_ready) return;
            string input = (SeedInput.Text ?? string.Empty).Trim();
            if (input.Length == 0)
            {
                SeedOutput.Text = "数字种子：";
                return;
            }

            if (long.TryParse(input, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long numeric))
            {
                SeedOutput.Text = "数字种子：" + numeric.ToString(CultureInfo.InvariantCulture) +
                                  "\n（输入的就是数字，游戏里直接用即可）";
                return;
            }

            int hash = JavaStringHash(input);
            SeedOutput.Text = "文字种子： " + input + "\n数字种子： " + hash.ToString(CultureInfo.InvariantCulture) +
                              "\n（粘贴到创建世界界面的种子栏）";
        }

        /// <summary>Java 的 String.hashCode()，Minecraft 就是用它把文字种子转成数字种子。</summary>
        private static int JavaStringHash(string value)
        {
            int hash = 0;
            foreach (char c in value) hash = unchecked(31 * hash + c);
            return hash;
        }

        private void CopySeed_Click(object sender, RoutedEventArgs e)
        {
            string input = (SeedInput.Text ?? string.Empty).Trim();
            if (long.TryParse(input, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long numeric))
                CopyToClipboard(numeric.ToString(CultureInfo.InvariantCulture), "种子");
            else
                CopyToClipboard(JavaStringHash(input).ToString(CultureInfo.InvariantCulture), "种子");
        }

        // ─────────────── 文本 / 编码 ───────────────
        private void Base64Encode_Click(object sender, RoutedEventArgs e)
            => RunTextTool(t => Convert.ToBase64String(Encoding.UTF8.GetBytes(t)));

        private void Base64Decode_Click(object sender, RoutedEventArgs e)
            => RunTextTool(t => Encoding.UTF8.GetString(Convert.FromBase64String(t.Trim())));

        private void UrlEncode_Click(object sender, RoutedEventArgs e)
            => RunTextTool(t => Uri.EscapeDataString(t));

        private void UrlDecode_Click(object sender, RoutedEventArgs e)
            => RunTextTool(t => Uri.UnescapeDataString(t));

        private void JsonPretty_Click(object sender, RoutedEventArgs e)
            => RunTextTool(t => JToken.Parse(t).ToString(Newtonsoft.Json.Formatting.Indented));

        private void RunTextTool(Func<string, string> action)
        {
            try
            {
                TextToolOutput.Text = action(TextToolInput.Text ?? string.Empty);
            }
            catch (Exception ex)
            {
                TextToolOutput.Text = "处理失败：" + ex.Message;
            }
        }

        private void ClearTextTool_Click(object sender, RoutedEventArgs e)
        {
            TextToolInput.Text = string.Empty;
            TextToolOutput.Text = string.Empty;
        }

        private void CopyToClipboard(string text, string what)
        {
            try
            {
                if (string.IsNullOrEmpty(text)) return;
                Clipboard.SetText(text);
                iOS26Dialog.Show(what + " 已复制到剪贴板。", "已复制", DialogIcon.Info, DialogButtons.OK);
            }
            catch (Exception ex)
            {
                iOS26Dialog.Show("复制失败: " + ex.Message, "错误", DialogIcon.Error, DialogButtons.OK);
            }
        }
    }
}
