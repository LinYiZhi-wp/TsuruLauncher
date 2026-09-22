using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TsuruLauncher.Services.Animation;
using TsuruLauncher.Utilities;
using TsuruLauncher.ViewModels;

namespace TsuruLauncher.Views
{
    /// <summary>
    /// 实例库页面（DownloadPage）：主体是本地实例列表（分段胶囊筛选 + 行卡片 + 空状态），
    /// 右上角「+ 创建实例」打开页面内嵌的创建实例向导（图标 / 名称 / 游戏目录 / 版本 / 加载器 + 安装步骤条）。
    /// </summary>
    public partial class DownloadPage : Page
    {
        public DownloadPage()
        {
            InitializeComponent();
            Loaded += OnPageLoaded;
            Loaded += OnWizardPageLoaded;
            Unloaded += OnWizardPageUnloaded;
        }

        #region 创建实例向导开 / 关（遮罩淡入 + 面板过冲上浮）

        // ── 数值全部取自 AXOLOTL_MOTION_PARAMS.md「弹窗 / 浮层」那一组的原始值 ──
        //    遮罩：opacity 0 <-> 0.72，200ms ease-out / ease-in
        //    面板：250ms cubic-bezier(0.15, 1.4, 0.64, 0.96)（真过冲），scale 0.96 -> 1 + translateY 12px -> 0
        private const double WizardMaskMs = AxolotlMotion.OverlayMaskMs;               // 200ms
        private const double WizardPanelEnterMs = AxolotlMotion.ContentSwitchMs;       // 250ms
        private const double WizardPanelLeaveMs = AxolotlMotion.WizardPanelLeaveMs;    // 200ms
        private const double WizardPanelFromScale = AxolotlMotion.ModalPanelFromScale; // 0.96
        private const double WizardPanelOffsetPx = 12.0;                               // translateY 12 -> 0
        /// <summary>页内看门狗比动画本身多等的时间：动画 250ms + 160ms 后仍未落到终态就强制落地。</summary>
        private const double WizardWatchdogExtraMs = 160.0;
        /// <summary>第一层兜底定时器比动画本身多等的时间（Completed 不来也要落终态）。</summary>
        private const double WizardSettleExtraMs = 60.0;

        private bool? _appliedWizardOpen;

        /// <summary>播放轮次号：每次开 / 关都 +1，上一轮的 Completed / 兜底定时器 / 看门狗全部作废。</summary>
        private int _wizardPlayToken;

        /// <summary>
        /// 「点背景关闭」的连点保护窗口：向导刚打开的这一小段里落到遮罩上的点击，
        /// 其实是双击「+ 创建实例」的第二下（第一下已经把向导打开了），不能当成「点背景关闭」，
        /// 否则连点会看到向导一闪而过。
        /// </summary>
        private const double WizardBackdropGraceMs = 420.0;

        private double _wizardOpenedAtMs;

        private DispatcherTimer? _wizardWatchdog;
        private bool _wizardWatchdogOpen;
        private int _wizardWatchdogToken;
        private double _wizardPlayStartedMs;

        /// <summary>第一层兜底：本轮动画的落终态定时器（Completed 因为任何原因不来也必须落地）。</summary>
        private DispatcherTimer? _wizardSettleTimer;
        private int _wizardSettleToken;

        /// <summary>
        /// [r4] 向导动画**不再经过 <c>PageTransition.PlayPopup</c>**。
        ///
        /// 根因（用户机日志 + 复现）：<c>PlayPopup</c> 内部的 <c>CacheDuring</c> 会给整页高的
        /// <c>WizardPanel</c> 挂一张 ≈0.92M 设备像素的 <see cref="System.Windows.Media.BitmapCache"/>。
        /// 这台机器（150% DPI / D3D renderTier=2）上「整页根挂大位图缓存」会让渲染线程雪崩
        /// （同一个坑在 MotionAssist 的 CacheSafe 注释里有完整记录）：
        /// 打开 -> 关掉 -> 再打开（第 3 次进向导）时 <c>UCEERR_RENDERTHREADFAILURE (0x88980406)</c>
        /// 连续抛出，窗口停在最后合成的那一帧、点什么都没反应 —— 就是用户报的「卡死」。
        ///
        /// 现在遮罩 / 面板的进出场由本文件**直接驱动自己的动画时钟**：
        ///   * 只动 <c>Opacity</c> 与 <c>RenderTransform</c>（TranslateTransform.Y / ScaleTransform）；
        ///   * **一个 BitmapCache 都不挂**（连 <c>cache-skip</c> 都不会出现）；
        ///   * 每次开 / 关前先把上一轮的时钟、兜底定时器、看门狗全部复位（可重入安全）。
        ///
        /// <b>三层兜底（缺一不可）</b>：
        ///   ① 本文件的一次性兜底定时器 <see cref="ArmWizardSettleFallback"/>（动画 250ms + 60ms）；
        ///   ② 播放轮次号 <see cref="_wizardPlayToken"/>（旧一轮的 Completed / 定时器不会覆盖新一轮）；
        ///   ③ 页内看门狗 <see cref="OnWizardWatchdogTick"/>（(面板时长 + 160ms) 到点检查实际属性值）。
        /// 任何异常、时钟停摆、Completed 丢失，最终都只会落到「完全不透明 + 面板归位」或「全透明 + Collapsed」，
        /// 绝不会停在半透明、也不会让遮罩留在可视树上吞掉点击。
        /// </summary>
        private void OnWizardPageLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is DownloadViewModel vm)
            {
                vm.PropertyChanged -= OnDownloadViewModelPropertyChanged;
                vm.PropertyChanged += OnDownloadViewModelPropertyChanged;
                ApplyWizardState(vm.IsWizardOpen, animate: false);
            }
        }

        private void OnWizardPageUnloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is DownloadViewModel vm)
                vm.PropertyChanged -= OnDownloadViewModelPropertyChanged;

            // 页面离场：作废当前轮次、停掉看门狗与兜底定时器，并且**保证遮罩不会以半透明状态留在可视树上**
            //（这正是「覆盖层停在半透明、整页发白、点什么都没反应」那类残留的入口）。
            _wizardPlayToken++;
            ResetWizardMotionClocks();
            try
            {
                if (WizardOverlay != null && WizardPanel != null && WizardMask != null)
                {
                    PageTransition.SettleOpacity(WizardMask, 0.0);
                    PageTransition.SettleOpacity(WizardPanel, 0.0);
                    WizardOverlay.Visibility = Visibility.Collapsed;
                }
            }
            catch { }
        }

        private void OnDownloadViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(DownloadViewModel.IsWizardOpen)) return;
            ApplyWizardState((DataContext as DownloadViewModel)?.IsWizardOpen == true, animate: true);
        }

        /// <summary>
        /// 向导开 / 关。
        ///
        /// 打开：遮罩 opacity 0 -> 0.72，200ms ease-out；面板 250ms
        /// <c>cubic-bezier(0.15, 1.4, 0.64, 0.96)</c>（真过冲）从 scale(0.96) + translateY(12px) + opacity 0 浮入。
        /// 关闭：两者都反向 200ms ease。
        /// 全程只动 Opacity / RenderTransform，动画结束后才把整层 Visibility 落成 Collapsed —— 不是硬切。
        ///
        /// <b>可重入安全</b>：进入时无条件 <see cref="ResetWizardMotionClocks"/> —— 上一轮留下的
        /// 动画时钟 / 兜底定时器 / 看门狗在这一刻全部作废，所以「开 -> 关 -> 开」这种反复切换
        /// 永远不会出现「旧一轮的收尾把新一轮的状态覆盖掉」或「定时器越积越多」。
        /// </summary>
        private void ApplyWizardState(bool open, bool animate)
        {
            if (WizardOverlay == null || WizardPanel == null || WizardMask == null) return;

            // ① 先复位上一轮的一切（时钟 / 兜底定时器 / 看门狗 / 轮次号）
            ResetWizardMotionClocks();

            bool changed = _appliedWizardOpen != open;
            _appliedWizardOpen = open;

            int token = ++_wizardPlayToken;   // 作废上一轮

            if (!animate || !changed)
            {
                if (open) _wizardOpenedAtMs = MotionPerf.NowMs;
                ApplyWizardTerminalState(open, "immediate");
                return;
            }

            try
            {
                using (MotionPerf.Measure("wizard-toggle", open ? "open" : "close"))
                {
                    if (open)
                    {
                        WizardOverlay.Visibility = Visibility.Visible;
                        _wizardOpenedAtMs = MotionPerf.NowMs;   // 遮罩连点保护窗口的起点
                        try { WizardOverlay.Focus(); } catch { }  // Esc 关闭要有键盘焦点落点
                    }

                    _wizardPlayStartedMs = MotionPerf.NowMs;

                    PlayWizardMotion(open, token);

                    // 第三层：页内看门狗。
                    ArmWizardWatchdog(open, token);
                }

                Logger.LogInfo(
                    $"[MotionPerf] wizard {(open ? "open" : "close")} " +
                    $"mask {WizardMaskMs}ms {(open ? "ease-out" : "ease-in")} -> {AxolotlMotion.ModalMaskOpacity} ; " +
                    $"panel {(open ? WizardPanelEnterMs : WizardPanelLeaveMs)}ms " +
                    $"{(open ? "cubic-bezier(0.15,1.4,0.64,0.96)" : "ease")} " +
                    (open
                        ? $"scale {WizardPanelFromScale}->1 translateY {WizardPanelOffsetPx}px->0 "
                        : $"scale 1->{WizardPanelFromScale} translateY 0->{WizardPanelOffsetPx}px ") +
                    $"watchdog={(open ? WizardPanelEnterMs : WizardPanelLeaveMs) + WizardWatchdogExtraMs}ms " +
                    $"settle={(open ? WizardPanelEnterMs : WizardPanelLeaveMs) + WizardSettleExtraMs}ms " +
                    $"cache=none(direct-clocks) (animations={PageTransition.AnimationsEnabled})");
            }
            catch (Exception ex)
            {
                // 播放本身炸了也必须落到终态 —— 不允许停在半透明。
                Logger.LogError(ex, "wizard-toggle");
                ApplyWizardTerminalState(open, "exception");
            }
        }

        /// <summary>
        /// 本轮动画：遮罩与面板各起一条**本地**动画（不走 PageTransition、不挂 BitmapCache）。
        /// 面板的淡入淡出 <c>Completed</c> 是正常收尾入口，兜底定时器见 <see cref="ArmWizardSettleFallback"/>。
        /// </summary>
        private void PlayWizardMotion(bool open, int token)
        {
            double maskMs = WizardMaskMs;
            double panelMs = open ? WizardPanelEnterMs : WizardPanelLeaveMs;

            // ── 遮罩：只动 Opacity ──────────────────────────────────────────────
            WizardMask.BeginAnimation(UIElement.OpacityProperty, null);
            double maskFrom = open ? WizardMask.Opacity : WizardMask.Opacity;
            double maskTo = open ? AxolotlMotion.ModalMaskOpacity : 0.0;
            WizardMask.Opacity = maskFrom;

            if (!PageTransition.AnimationsEnabled || Math.Abs(maskTo - maskFrom) < 0.0001)
            {
                WizardMask.Opacity = maskTo;
            }
            else
            {
                var maskAnim = new DoubleAnimation(maskFrom, maskTo, DurationOf(maskMs))
                {
                    EasingFunction = open ? AxolotlMotion.EaseOut : AxolotlMotion.EaseIn
                };
                WizardMask.BeginAnimation(UIElement.OpacityProperty, maskAnim);
            }

            // ── 面板：只动 Opacity + RenderTransform（scale / translateY）────────
            var scale = EnsureTransform<ScaleTransform>(WizardPanel);
            var translate = EnsureTransform<TranslateTransform>(WizardPanel);

            WizardPanel.BeginAnimation(UIElement.OpacityProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            translate.BeginAnimation(TranslateTransform.XProperty, null);
            translate.BeginAnimation(TranslateTransform.YProperty, null);

            double from = open ? 0.0 : Math.Max(0.0, WizardPanel.Opacity);
            double to = open ? 1.0 : 0.0;
            double scaleFrom = open ? WizardPanelFromScale : 1.0;
            double scaleTo = open ? 1.0 : WizardPanelFromScale;
            double yFrom = open ? WizardPanelOffsetPx : 0.0;
            double yTo = open ? 0.0 : WizardPanelOffsetPx;

            // 基值先写成起点：万一时钟被清掉，元素也是停在这一轮的起点上（而不是上一轮的终点）。
            WizardPanel.Opacity = from;
            scale.ScaleX = scale.ScaleY = scaleFrom;
            translate.X = 0;
            translate.Y = yFrom;

            if (!PageTransition.AnimationsEnabled)
            {
                ApplyWizardTerminalState(open, "no-animation");
                return;
            }

            var panelEase = open ? AxolotlMotion.OvershootEase : AxolotlMotion.Ease;
            var dur = DurationOf(panelMs);

            DoubleAnimation? fade = null;
            if (Math.Abs(to - from) > 0.0001)
            {
                fade = new DoubleAnimation(from, to, dur) { EasingFunction = panelEase };
                fade.Completed += (_, __) => OnWizardAnimationSettled(open, token);
                WizardPanel.BeginAnimation(UIElement.OpacityProperty, fade);
            }

            if (Math.Abs(scaleTo - scaleFrom) > 0.0001)
            {
                var zoom = new DoubleAnimation(scaleFrom, scaleTo, dur) { EasingFunction = panelEase };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);
            }

            if (Math.Abs(yTo - yFrom) > 0.0001)
            {
                var slide = new DoubleAnimation(yFrom, yTo, dur) { EasingFunction = panelEase };
                translate.BeginAnimation(TranslateTransform.YProperty, slide);
            }

            // 第一层兜底：面板那条淡入淡出没起（理论上不会）也要有人收尾。
            ArmWizardSettleFallback(open, token, Math.Max(maskMs, panelMs));

            if (fade == null)
                OnWizardAnimationSettled(open, token);
        }

        private static Duration DurationOf(double ms) => new Duration(TimeSpan.FromMilliseconds(Math.Max(1.0, ms)));

        /// <summary>
        /// 动画（Completed 或兜底定时器）声称自己跑完了：只有还是当前轮次才认。
        ///
        /// 注意这里**必须无条件落一次终态**（不能因为「看起来已经到位」就跳过）：
        /// 动画的 FillBehavior=HoldEnd 只是把**动画值**保持在终点，元素上的**基值**仍然是
        /// 本轮起点（打开时是 opacity=0 / scale=0.96 / translateY=12）。一旦下一轮开头
        /// <see cref="ResetWizardMotionClocks"/> 清掉时钟，基值就会重新生效 ——
        /// 那样「关掉向导」会变成瞬间消失（没有 200ms 反向动画），严重时还会闪一下。
        /// 落终态会把基值写成终点（opacity 1 或 0 / scale 1 / translate 0），从根上避免这件事。
        /// </summary>
        private void OnWizardAnimationSettled(bool open, int token)
        {
            if (token != _wizardPlayToken) return;   // 已被新一轮接管：不要用旧终态覆盖新动画
            ApplyWizardTerminalState(open, "settled");
        }

        /// <summary>
        /// 第一层兜底：<paramref name="ms"/> + 60ms 之后本轮仍未落终态，就强制落地并留一行
        /// <c>wizard-settle-fallback</c>（Completed 因为任何原因不来的最后一道保险）。
        /// </summary>
        private void ArmWizardSettleFallback(bool open, int token, double ms)
        {
            try
            {
                _wizardSettleTimer?.Stop();
                _wizardSettleToken = token;
                _wizardSettleTimer = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(Math.Max(1.0, ms) + WizardSettleExtraMs),
                    DispatcherPriority.Background,
                    (_, __) => OnWizardSettleFallback(open, token),
                    Dispatcher);
                _wizardSettleTimer.Start();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "wizard-settle-arm");
                ApplyWizardTerminalState(open, "settle-arm-failed");
            }
        }

        private void OnWizardSettleFallback(bool open, int token)
        {
            try { _wizardSettleTimer?.Stop(); } catch { }

            if (token != _wizardSettleToken) return;   // 已被新一轮换掉
            if (token != _wizardPlayToken) return;     // 已被新一轮接管

            bool atTerminal = IsWizardAtTerminal(open);
            if (!atTerminal)
            {
                Logger.LogWarning(
                    $"[MotionPerf] wizard-settle-fallback open={open} " +
                    $"mask={WizardMask.Opacity:0.###} panel={WizardPanel.Opacity:0.###} " +
                    $"scale={ScaleOf(WizardPanel):0.###} translateY={TranslateYOf(WizardPanel):0.###} " +
                    $"visibility={WizardOverlay.Visibility} token={_wizardPlayToken} " +
                    $"elapsed={MotionPerf.NowMs - _wizardPlayStartedMs:0}ms -> force-settle");
            }

            // 即使「看起来已经到位」也要落一次：动画值停在终点，但基值还是本轮起点，
            // 不写到位的话下一轮清时钟时会闪回起点（见 OnWizardAnimationSettled 的注释）。
            ApplyWizardTerminalState(open, atTerminal ? "settled" : "settle-fallback");
        }

        /// <summary>
        /// 把「上一轮」的一切残留清干净：动画时钟、兜底定时器、看门狗。
        /// 每次开 / 关、每次页面离场都会先走这一步（可重入安全的入口）。
        /// </summary>
        private void ResetWizardMotionClocks()
        {
            try { _wizardSettleTimer?.Stop(); } catch { }
            try { _wizardWatchdog?.Stop(); } catch { }

            try
            {
                if (WizardMask != null)
                    WizardMask.BeginAnimation(UIElement.OpacityProperty, null);

                if (WizardPanel != null)
                {
                    WizardPanel.BeginAnimation(UIElement.OpacityProperty, null);
                    ClearTransformClocks(WizardPanel.RenderTransform);
                }
            }
            catch { }
        }

        /// <summary>把面板的 RenderTransform 强制归位（scale 1 / translateY 0）—— 关闭后的静止态。</summary>
        private void ResetWizardPanelTransforms()
        {
            if (WizardPanel == null) return;

            var scale = FindTransform<ScaleTransform>(WizardPanel.RenderTransform);
            if (scale != null)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = 1.0;
                scale.ScaleY = 1.0;
            }

            var translate = FindTransform<TranslateTransform>(WizardPanel.RenderTransform);
            if (translate != null)
            {
                translate.BeginAnimation(TranslateTransform.XProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                translate.X = 0.0;
                translate.Y = 0.0;
            }
        }

        /// <summary>只清时钟、不写值（基值留在上一次写下的位置，下一轮会重新写起点）。</summary>
        private static void ClearTransformClocks(Transform? transform)
        {
            switch (transform)
            {
                case null:
                    return;
                case TransformGroup group:
                    foreach (var child in group.Children) ClearTransformClocks(child);
                    return;
                case TranslateTransform translate:
                    translate.BeginAnimation(TranslateTransform.XProperty, null);
                    translate.BeginAnimation(TranslateTransform.YProperty, null);
                    return;
                case ScaleTransform scale:
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    return;
            }
        }

        /// <summary>在 RenderTransform 里找出第一个 T；没有就在 TransformGroup 里补一个（不挂任何缓存）。</summary>
        private static T EnsureTransform<T>(FrameworkElement element) where T : Transform, new()
        {
            var found = FindTransform<T>(element.RenderTransform);
            if (found != null) return found;

            var created = new T();
            switch (element.RenderTransform)
            {
                case null:
                    element.RenderTransform = new TransformGroup { Children = { created } };
                    break;
                case TransformGroup group:
                    group.Children.Add(created);
                    break;
                default:
                    element.RenderTransform = new TransformGroup { Children = { element.RenderTransform, created } };
                    break;
            }
            return created;
        }

        /// <summary>
        /// 把向导层强制落到终态（清时钟 + 写基值 + 切 Visibility）。
        /// 这是唯一一处真正决定「打开 / 关闭长什么样」的地方 —— 动画、兜底、看门狗都汇到这里。
        /// </summary>
        private void ApplyWizardTerminalState(bool open, string reason)
        {
            try
            {
                if (WizardOverlay == null || WizardPanel == null || WizardMask == null) return;

                try { _wizardSettleTimer?.Stop(); } catch { }
                try { _wizardWatchdog?.Stop(); } catch { }

                if (open)
                {
                    WizardOverlay.Visibility = Visibility.Visible;
                    PageTransition.SettleOpacity(WizardMask, AxolotlMotion.ModalMaskOpacity);
                    PageTransition.SettleElement(WizardPanel);   // Opacity=1 / translate=0 / scale=1
                }
                else
                {
                    PageTransition.SettleOpacity(WizardMask, 0.0);
                    PageTransition.SettleOpacity(WizardPanel, 0.0);
                    ResetWizardPanelTransforms();   // 结束强制归位：scale 1 / translateY 0，不留 0.96 / 12px 残留
                    WizardOverlay.Visibility = Visibility.Collapsed;
                }

                Logger.LogInfo(
                    $"[MotionPerf] wizard-settle {reason} open={open} " +
                    $"mask={WizardMask.Opacity:0.###} panel={WizardPanel.Opacity:0.###} " +
                    $"scale={ScaleOf(WizardPanel):0.###} translateY={TranslateYOf(WizardPanel):0.###} " +
                    $"visibility={(open ? "Visible" : "Collapsed")} " +
                    $"token={_wizardPlayToken} elapsed={MotionPerf.NowMs - _wizardPlayStartedMs:0}ms");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "wizard-settle");
            }
        }

        private void ArmWizardWatchdog(bool open, int token)
        {
            try
            {
                if (_wizardWatchdog == null)
                {
                    _wizardWatchdog = new DispatcherTimer(DispatcherPriority.Normal, Dispatcher);
                    _wizardWatchdog.Tick += OnWizardWatchdogTick;
                }

                _wizardWatchdogOpen = open;
                _wizardWatchdogToken = token;
                _wizardWatchdog.Stop();
                _wizardWatchdog.Interval = TimeSpan.FromMilliseconds(
                    (open ? WizardPanelEnterMs : WizardPanelLeaveMs) + WizardWatchdogExtraMs);
                _wizardWatchdog.Start();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "wizard-watchdog-arm");
                ApplyWizardTerminalState(open, "watchdog-arm-failed");
            }
        }

        /// <summary>
        /// 页内看门狗（第三层）：动画应该在 (面板时长 + 160ms) 内落到终态。
        /// 到点了还没落（时钟被清掉 / Completed 没来 / 渲染线程卡住 / 任何异常路径），
        /// 就直接强制落地并留一行 <c>wizard-watchdog</c> 日志 —— 便于事后 grep 判定兜底是否生效。
        /// </summary>
        private void OnWizardWatchdogTick(object? sender, EventArgs e)
        {
            try { _wizardWatchdog?.Stop(); } catch { }

            if (_wizardWatchdogToken != _wizardPlayToken) return;   // 已被新一轮开关接管

            bool atTerminal = IsWizardAtTerminal(_wizardWatchdogOpen);
            if (!atTerminal)
            {
                Logger.LogWarning(
                    $"[MotionPerf] wizard-watchdog fired open={_wizardWatchdogOpen} " +
                    $"mask={WizardMask.Opacity:0.###} panel={WizardPanel.Opacity:0.###} " +
                    $"scale={ScaleOf(WizardPanel):0.###} translateY={TranslateYOf(WizardPanel):0.###} " +
                    $"visibility={WizardOverlay.Visibility} token={_wizardPlayToken} " +
                    $"elapsed={MotionPerf.NowMs - _wizardPlayStartedMs:0}ms -> force-settle");
            }

            // 兜底也要把**基值**写到位（不只是看起来到位），否则被打断时元素会停在本轮起点。
            ApplyWizardTerminalState(_wizardWatchdogOpen, atTerminal ? "settled" : "watchdog");
        }

        /// <summary>终态判定：不透明度和「缩放 / 位移是否归位」都要检查（半透明 = 不合格）。</summary>
        private bool IsWizardAtTerminal(bool open)
        {
            try
            {
                if (WizardOverlay == null || WizardPanel == null || WizardMask == null) return true;

                if (open)
                {
                    if (WizardOverlay.Visibility != Visibility.Visible) return false;
                    if (Math.Abs(WizardMask.Opacity - AxolotlMotion.ModalMaskOpacity) > 0.02) return false;
                    if (Math.Abs(WizardPanel.Opacity - 1.0) > 0.02) return false;
                    if (Math.Abs(ScaleOf(WizardPanel) - 1.0) > 0.01) return false;
                    if (Math.Abs(TranslateYOf(WizardPanel)) > 0.5) return false;
                }
                else
                {
                    if (WizardOverlay.Visibility != Visibility.Collapsed) return false;
                    if (WizardMask.Opacity > 0.02) return false;
                    if (WizardPanel.Opacity > 0.02) return false;
                }

                return true;
            }
            catch { return true; }
        }

        /// <summary>点遮罩背景 = 关闭向导（和 X / Esc / 返回 走同一条 CloseWizard 路径，动画与兜底完全一致）。</summary>
        private void OnWizardMaskMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (MotionPerf.NowMs - _wizardOpenedAtMs < WizardBackdropGraceMs)
                {
                    MotionPerf.NoteSkip("wizard-backdrop-click", "double-click-grace");
                    return;
                }

                RequestCloseWizard("backdrop");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "wizard-backdrop");
            }
        }

        /// <summary>Esc = 关闭向导（向导打开时焦点落在遮罩层上，键盘事件能到这一层）。</summary>
        private void OnWizardPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            try
            {
                if (e.Key != System.Windows.Input.Key.Escape) return;
                if (DataContext is not DownloadViewModel vm || !vm.IsWizardOpen) return;

                e.Handled = true;
                RequestCloseWizard("esc");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "wizard-esc");
            }
        }

        private void RequestCloseWizard(string reason)
        {
            if (DataContext is not DownloadViewModel vm) return;
            if (!vm.CloseWizardCommand.CanExecute(null)) return;

            Logger.LogInfo($"[MotionPerf] wizard-close-request reason={reason}");
            vm.CloseWizardCommand.Execute(null);
        }

        private static double ScaleOf(FrameworkElement element)
        {
            var scale = FindTransform<ScaleTransform>(element.RenderTransform);
            return scale?.ScaleX ?? 1.0;
        }

        private static double TranslateYOf(FrameworkElement element)
        {
            var translate = FindTransform<TranslateTransform>(element.RenderTransform);
            return translate?.Y ?? 0.0;
        }

        /// <summary>在 RenderTransform（可能是 TransformGroup）里找出第一个 T 变换。</summary>
        private static T? FindTransform<T>(Transform? transform) where T : Transform
        {
            switch (transform)
            {
                case null:
                    return null;
                case T match:
                    return match;
                case TransformGroup group:
                    foreach (var child in group.Children)
                    {
                        var found = FindTransform<T>(child);
                        if (found != null) return found;
                    }
                    return null;
                default:
                    return null;
            }
        }

        #endregion

        /// <summary>每次回到本页都重新扫描一遍实例库，避免显示过期数据（扫描本身在线程池上，不挡 UI 线程）。</summary>
        private void OnPageLoaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is DownloadViewModel vm)
            {
                vm.RefreshInstancesCommand.Execute(null);
            }
        }

        private void OpenDownloadManager_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService != null)
            {
                NavigationService.Navigate(new DownloadManagerPage());
            }
        }
    }
}
