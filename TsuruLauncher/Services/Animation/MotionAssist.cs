using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TsuruLauncher.Services.Animation
{
    /// <summary>XAML 里可选的缓动档位，映射到 <see cref="AxolotlMotion"/> 里冻结好的 CSS 曲线。</summary>
    public enum MotionEase
    {
        /// <summary>CSS ease = cubic-bezier(0.25, 0.1, 0.25, 1)。</summary>
        Ease,
        /// <summary>CSS/Tailwind ease-in-out = cubic-bezier(0.4, 0, 0.2, 1)。</summary>
        EaseInOut,
        /// <summary>Tailwind ease-out = cubic-bezier(0, 0, 0.2, 1)。</summary>
        EaseOut,
        /// <summary>Tailwind ease-in = cubic-bezier(0.4, 0, 1, 1)。</summary>
        EaseIn,
        /// <summary>cubic-bezier(0.15, 1.4, 0.64, 0.96) —— 会过冲。</summary>
        Overshoot,
        /// <summary>cubic-bezier(0.51, 1.08, 0.35, 1.15)。</summary>
        PopupIn,
        /// <summary>cubic-bezier(0.68, -0.17, 0.23, 0.11)。</summary>
        PopupOut,
        /// <summary>cubic-bezier(0.22, 1, 0.36, 1)。</summary>
        Sidebar,
    }

    /// <summary>
    /// 只动 <see cref="UIElement.RenderTransform"/> 与 <see cref="UIElement.OpacityProperty"/> 的
    /// 通用动效附加属性 —— 全部按 Axolotl 的真实参数执行，绝不触碰 Width/Height/Margin。
    ///
    /// 用法（XAML）：
    ///   &lt;Border local:MotionAssist.HoverScale="1.03" local:MotionAssist.PressScale="0.98"/&gt;
    ///   &lt;Button local:MotionAssist.PressScale="0.95" local:MotionAssist.PressScaleMs="125"/&gt;
    /// </summary>
    public static class MotionAssist
    {
        #region HoverScale

        public static readonly DependencyProperty HoverScaleProperty =
            DependencyProperty.RegisterAttached("HoverScale", typeof(double), typeof(MotionAssist),
                new PropertyMetadata(0.0, OnMotionChanged));

        public static double GetHoverScale(DependencyObject o) => (double)o.GetValue(HoverScaleProperty);
        public static void SetHoverScale(DependencyObject o, double v) => o.SetValue(HoverScaleProperty, v);

        public static readonly DependencyProperty HoverScaleMsProperty =
            DependencyProperty.RegisterAttached("HoverScaleMs", typeof(double), typeof(MotionAssist),
                new PropertyMetadata(150.0));

        public static double GetHoverScaleMs(DependencyObject o) => (double)o.GetValue(HoverScaleMsProperty);
        public static void SetHoverScaleMs(DependencyObject o, double v) => o.SetValue(HoverScaleMsProperty, v);

        public static readonly DependencyProperty HoverScaleEaseProperty =
            DependencyProperty.RegisterAttached("HoverScaleEase", typeof(MotionEase), typeof(MotionAssist),
                new PropertyMetadata(MotionEase.EaseInOut));

        public static MotionEase GetHoverScaleEase(DependencyObject o) => (MotionEase)o.GetValue(HoverScaleEaseProperty);
        public static void SetHoverScaleEase(DependencyObject o, MotionEase v) => o.SetValue(HoverScaleEaseProperty, v);

        #endregion

        #region PressScale

        public static readonly DependencyProperty PressScaleProperty =
            DependencyProperty.RegisterAttached("PressScale", typeof(double), typeof(MotionAssist),
                new PropertyMetadata(0.0, OnMotionChanged));

        public static double GetPressScale(DependencyObject o) => (double)o.GetValue(PressScaleProperty);
        public static void SetPressScale(DependencyObject o, double v) => o.SetValue(PressScaleProperty, v);

        public static readonly DependencyProperty PressScaleMsProperty =
            DependencyProperty.RegisterAttached("PressScaleMs", typeof(double), typeof(MotionAssist),
                new PropertyMetadata(125.0));

        public static double GetPressScaleMs(DependencyObject o) => (double)o.GetValue(PressScaleMsProperty);
        public static void SetPressScaleMs(DependencyObject o, double v) => o.SetValue(PressScaleMsProperty, v);

        public static readonly DependencyProperty PressScaleEaseProperty =
            DependencyProperty.RegisterAttached("PressScaleEase", typeof(MotionEase), typeof(MotionAssist),
                new PropertyMetadata(MotionEase.EaseInOut));

        public static MotionEase GetPressScaleEase(DependencyObject o) => (MotionEase)o.GetValue(PressScaleEaseProperty);
        public static void SetPressScaleEase(DependencyObject o, MotionEase v) => o.SetValue(PressScaleEaseProperty, v);

        #endregion

        #region RevealOnHover（卡片角标：scale-75/opacity-0 -> scale-100/opacity-100）

        public static readonly DependencyProperty RevealOnHoverProperty =
            DependencyProperty.RegisterAttached("RevealOnHover", typeof(bool), typeof(MotionAssist),
                new PropertyMetadata(false, OnMotionChanged));

        public static bool GetRevealOnHover(DependencyObject o) => (bool)o.GetValue(RevealOnHoverProperty);
        public static void SetRevealOnHover(DependencyObject o, bool v) => o.SetValue(RevealOnHoverProperty, v);

        public static readonly DependencyProperty RevealScaleProperty =
            DependencyProperty.RegisterAttached("RevealScale", typeof(double), typeof(MotionAssist),
                new PropertyMetadata(AxolotlMotion.CardBadgeFromScale));

        public static double GetRevealScale(DependencyObject o) => (double)o.GetValue(RevealScaleProperty);
        public static void SetRevealScale(DependencyObject o, double v) => o.SetValue(RevealScaleProperty, v);

        /// <summary>CSS <c>translate-y-1</c> = 4px；卡片角标从下方升起。</summary>
        public static readonly DependencyProperty RevealOffsetYProperty =
            DependencyProperty.RegisterAttached("RevealOffsetY", typeof(double), typeof(MotionAssist),
                new PropertyMetadata(0.0));

        public static double GetRevealOffsetY(DependencyObject o) => (double)o.GetValue(RevealOffsetYProperty);
        public static void SetRevealOffsetY(DependencyObject o, double v) => o.SetValue(RevealOffsetYProperty, v);

        public static readonly DependencyProperty RevealMsProperty =
            DependencyProperty.RegisterAttached("RevealMs", typeof(double), typeof(MotionAssist),
                new PropertyMetadata(150.0));

        public static double GetRevealMs(DependencyObject o) => (double)o.GetValue(RevealMsProperty);
        public static void SetRevealMs(DependencyObject o, double v) => o.SetValue(RevealMsProperty, v);

        #endregion

        #region CardCache（给动画容器挂 BitmapCache，避免整棵子树重绘）

        public static readonly DependencyProperty CardCacheProperty =
            DependencyProperty.RegisterAttached("CardCache", typeof(bool), typeof(MotionAssist),
                new PropertyMetadata(false, OnCacheChanged));

        public static bool GetCardCache(DependencyObject o) => (bool)o.GetValue(CardCacheProperty);
        public static void SetCardCache(DependencyObject o, bool v) => o.SetValue(CardCacheProperty, v);

        private static void OnCacheChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement fe) return;
            EnableBitmapCache(fe, (bool)e.NewValue);
        }

        #endregion

        #region CacheDisabled（整页浮层这类元素：动效期**不要**挂缓存）

        /// <summary>
        /// 附加属性：给这个元素关掉动效期的 <see cref="BitmapCache"/>（含下沉路径）。
        ///
        /// <para>什么时候必须用：元素面积贴着 1.2MP 安全线、而且**随后还要切 Visibility**
        /// （整页浮层进出就属于这一类）。这时缓存会下沉到浮层内部那块同样接近上限的面板
        /// （实测 <c>cache-on Grid#ToolDetailPanel rootPx=1281280 hostPx=1155592</c>），
        /// 而这个面板紧接着要被 Collapsed —— <b>缓存 + 布局变更会让渲染线程直接崩</b>：
        /// <c>UCEERR_RENDERTHREADFAILURE (0x88980406)</c>，表现就是点「返回」整窗卡死
        /// （HANDOFF §6.2 的同一个坑，只是触发元素换成了浮层内的面板）。</para>
        ///
        /// <para>关掉缓存后实测（实验室工具面板进出）：
        /// 进入 p95=16.13ms / max=16.86ms，退出 p95=20.97ms / max=23.51ms，0 崩溃。
        /// 代价只是每帧多一次栅格化 —— 比渲染线程崩掉便宜得多。</para>
        /// </summary>
        public static readonly DependencyProperty CacheDisabledProperty =
            DependencyProperty.RegisterAttached("CacheDisabled", typeof(bool), typeof(MotionAssist),
                new PropertyMetadata(false));

        public static bool GetCacheDisabled(DependencyObject o) => (bool)o.GetValue(CacheDisabledProperty);
        public static void SetCacheDisabled(DependencyObject o, bool v) => o.SetValue(CacheDisabledProperty, v);

        /// <summary>
        /// 开启/关闭 <see cref="BitmapCache"/>。
        ///
        /// 只在动效进行中开启，动画结束由 <c>PageTransition.SettleElement</c> / 摘除定时器关掉：
        /// 长期挂缓存会让文字发虚（缓存位图不会跟着系统 DPI 重新栅格化）。
        /// <c>RenderAtScale</c> 取当前 DPI 缩放，150% 缩放下缓存位图才是 1:1 像素、不会糊。
        /// </summary>
        /// <summary>
        /// 单个 BitmapCache 允许的最大**设备像素**面积（≈2.0 MP）—— 任务要求把旧上限（1.2MP）
        /// 抬到这里，好让主页那两棵各 1,281,280 设备像素的整页根不再被"差 6.7%"挡在缓存之外。
        ///
        /// <para><b>实测回退（重要）</b>：本机 A/B 实测证明"给这两棵整页根真的挂上 BitmapCache"
        /// 会让渲染线程雪崩 —— 第 1 次切换正常（worst 12.7ms），<b>第 2 次起</b>
        /// <c>[MotionPerf] home-mode-frames</c> 不再落盘，或 worst=1204.7 / 1236.4 / 1212.1 / 2467.2ms、
        /// total=1.9~6.0s，画面卡在旧视图 + 残影（连 pill 指示块都不再更新）——
        /// 正是下面这段注释里记的 <c>UCEERR_RENDERTHREADFAILURE (0x88980406)</c> 现场。
        /// 降倍率（0.75x DPI / RenderAtScale=1.0 绝对）、只缓存 incoming 不缓存 outgoing、
        /// 永不摘除缓存 —— 三种变体都照样复现，所以与倍率、位图大小、挂/摘时机都无关，
        /// 是"这两个整页根挂大位图缓存"本身在这台机器（150% DPI、1180x760 视口、
        /// D3D renderTier=2）上的硬限制。
        /// 因此默认走 <see cref="SafeCacheDevicePixels"/>（= 改前的 1.2MP，保证不回归），
        /// 任务要求的 2.0MP 全倍率 + 降倍率路径用 <c>TSURU_CACHE_BIG=1</c> 打开，便于随时 A/B。</para>
        /// </summary>
        public const double MaxCacheDevicePixels = 2_000_000;

        /// <summary>
        /// **降倍率挂缓存**的上限（≈4.0 MP）：设备像素落在
        /// (<see cref="MaxCacheDevicePixels"/>, <see cref="MaxCacheDevicePixelsDownscaled"/>]
        /// 区间里的元素不跳过缓存，而是按 <see cref="CacheDownscaleFactor"/> 降倍率挂一张小位图
        /// （150% DPI 下 RenderAtScale 1.125，位图内存约为全倍率的 56%）。
        /// 超过 4.0MP 才真的跳过：那种尺寸降倍率后仍是一张十几 MB 的位图。
        /// 只有 <c>TSURU_CACHE_BIG=1</c> 时这条路径才会被走到（默认见 <see cref="SafeCacheDevicePixels"/>）。
        /// </summary>
        public const double MaxCacheDevicePixelsDownscaled = 4_000_000;

        /// <summary>降倍率挂缓存时 <see cref="BitmapCache.RenderAtScale"/> 相对当前 DPI 的比例。</summary>
        public const double CacheDownscaleFactor = 0.75;

        /// <summary>
        /// 默认生效的**安全线**（= 改前的旧上限 1.2MP）。见 <see cref="MaxCacheDevicePixels"/> 的实测回退说明：
        /// 超过这条线的整页根位图缓存会让渲染线程在第 2 次切换起雪崩，所以默认仍然跳过它们，
        /// 保持改前的 <c>cache-skip ... limit=1200000</c> 行为。
        /// </summary>
        public const double SafeCacheDevicePixels = 1_200_000;

        /// <summary>
        /// <c>TSURU_CACHE_BIG=1</c>：启用任务要求的"2.0MP 全倍率 + 2.0~4.0MP 降倍率"缓存路径。
        /// 默认关闭（走 <see cref="SafeCacheDevicePixels"/>），因为实测那条路径会把主页切换打崩
        /// —— 详见 <see cref="MaxCacheDevicePixels"/> 的注释与 Q1 报告里的 A/B 数据。
        /// </summary>
        public static bool BigCacheEnabled
        {
            get
            {
                try { return string.Equals((Environment.GetEnvironmentVariable("TSURU_CACHE_BIG") ?? string.Empty).Trim(), "1", StringComparison.Ordinal); }
                catch { return false; }
            }
        }

        public static void EnableBitmapCache(FrameworkElement element, bool enabled, string? tagOverride = null)
        {
            if (element == null) return;
            string tag = string.IsNullOrEmpty(tagOverride) ? Describe(element) : tagOverride!;

            if (!enabled)
            {
                if (element.CacheMode is BitmapCache) MotionPerf.NoteCache(false, tag, 0);
                element.CacheMode = null;          // 结束即摘，不留任何"永久缓存"
                return;
            }
            if (element.CacheMode is BitmapCache) return;

            double w = element.ActualWidth, h = element.ActualHeight;
            if (double.IsNaN(w) || double.IsNaN(h) || w < 1 || h < 1) return;   // 没尺寸：缓存无意义

            double scale = 1.0;
            try
            {
                var dpi = VisualTreeHelper.GetDpi(element);
                scale = Math.Max(1.0, Math.Max(dpi.DpiScaleX, dpi.DpiScaleY));
            }
            catch { }

            double devicePixels = w * scale * h * scale;

            // 生效的两条线：默认 = 安全线（改前行为）；TSURU_CACHE_BIG=1 = 任务要求的上限。
            bool big = BigCacheEnabled;
            double fullScaleLimit = big ? MaxCacheDevicePixels : SafeCacheDevicePixels;
            double hardLimit = big ? MaxCacheDevicePixelsDownscaled : SafeCacheDevicePixels;

            // 超过 hardLimit：降倍率也不划算（或默认模式下就是不安全）-> 跳过。cache-skip 埋点带 px 与生效的 limit。
            if (devicePixels > hardLimit)
            {
                element.CacheMode = null;
                MotionPerf.NoteCacheSkipped(tag, devicePixels, hardLimit);
                return;
            }

            // fullScaleLimit ~ hardLimit：**降倍率挂缓存**，而不是干脆不挂。
            // RenderAtScale = 0.75 x DPI，但不低于 1.0（100% DPI 下再降就只剩糊了）。
            // （默认模式下这两条线相同，所以这条分支只有 TSURU_CACHE_BIG=1 时才会走到。）
            double renderAtScale = scale;
            string mode = "full";
            if (devicePixels > fullScaleLimit)
            {
                renderAtScale = Math.Max(1.0, scale * CacheDownscaleFactor);
                mode = "downscale";
            }

            element.CacheMode = new BitmapCache { RenderAtScale = renderAtScale, SnapsToDevicePixels = false };

            // cache-on 埋点必须带 px 与 limit。MotionPerf.NoteCache 的签名只有 (attached, element, px)，
            // 而 MotionPerf.cs 不在本次改动范围内，所以把 limit/mode/倍率拼进 element 字段，
            // 日志仍然是一行、形状不变：cache-on <元素> limit=<上限> mode=<full|downscale> px=<实际> ...
            MotionPerf.NoteCache(true,
                tag + " limit=" + (long)hardLimit + " mode=" + mode +
                " renderAtScale=" + renderAtScale.ToString("0.###") + " big=" + (big ? 1 : 0),
                devicePixels);
        }


        #region MotionHost（把 scale / BitmapCache 从「整页根」下沉到内层内容容器 —— 任务 r3）

        /// <summary>
        /// 动效（RenderTransform 缩放 + <see cref="BitmapCache"/>）允许作用的**最大设备像素面积**：
        /// 直接用 1.2MP 安全线（<see cref="SafeCacheDevicePixels"/>）。
        ///
        /// <para>为什么必须有这条线：主页那两棵整页根实测各 1,281,280 设备像素
        /// （1089x693 DIP @150%，正好比 1.2MP 大 6.7%）。在整页根上挂 BitmapCache 会让渲染线程崩
        /// （帧间隔 1.2s、卡死，见 <see cref="MaxCacheDevicePixels"/> 的实测记录）；
        /// 而在整页根上做**逐帧 scale** 的代价与它是同一量级 —— 渲染线程每帧都要重新栅格化
        /// 整棵 1.28MP 子树。所以 scale 与缓存都下沉到面积 ≤ 这条线的**内层内容容器**上；
        /// 实在降不下去时，该处**既不缓存也不做 scale**，只保留 opacity + translateY。</para>
        /// </summary>
        public const double MotionHostDevicePixelBudget = SafeCacheDevicePixels;

        /// <summary>
        /// 内层内容容器至少要覆盖根元素面积的这个比例，才算「内容容器」。
        /// 否则挑出来的可能只是某张小卡片 —— 只让一张卡缩放，观感反而比"不缩放"更糟。
        /// </summary>
        public const double MotionHostMinCoverage = 0.30;

        /// <summary>
        /// 下沉开关（A/B 用，默认开）。<c>TSURU_MOTION_SINK=0</c> 时 scale 与缓存**都不下沉**：
        /// 页面/内容切换只剩 opacity + translateY（任务 §1 的字面形态），同一份构建就能对比
        /// "下沉到内层容器 + 挂缓存" 与 "整页不做 scale 也不缓存" 两种做法的帧间隔。
        /// </summary>
        public static bool SinkEnabled
        {
            get
            {
                try
                {
                    return !string.Equals((Environment.GetEnvironmentVariable("TSURU_MOTION_SINK") ?? string.Empty).Trim(),
                        "0", StringComparison.Ordinal);
                }
                catch { return true; }
            }
        }

        /// <summary>解析内层容器时最多往下看几层 / 看几个节点（同步预算 3ms，必须封顶）。</summary>
        private const int MotionHostMaxDepth = 7;
        private const int MotionHostMaxNodes = 28;

        /// <summary>设备像素面积 = ActualWidth x ActualHeight x DPI 缩放²；没尺寸（NaN/&lt;1）时返回 0。</summary>
        public static double DevicePixelArea(FrameworkElement? element)
            => DevicePixelArea(element, DpiScaleOf(element));

        /// <summary>同一棵可视树里的元素共用同一个 DPI 缩放 —— 解析时只算一次，别每个候选都问一遍。</summary>
        private static double DpiScaleOf(FrameworkElement? element)
        {
            try
            {
                if (element == null) return 1.0;
                var dpi = VisualTreeHelper.GetDpi(element);
                return Math.Max(1.0, Math.Max(dpi.DpiScaleX, dpi.DpiScaleY));
            }
            catch { return 1.0; }
        }

        private static double DevicePixelArea(FrameworkElement? element, double scale)
        {
            if (element == null) return 0.0;
            try
            {
                double w = element.ActualWidth, h = element.ActualHeight;
                if (double.IsNaN(w) || double.IsNaN(h) || w < 1.0 || h < 1.0) return 0.0;
                return w * scale * h * scale;
            }
            catch { return 0.0; }
        }

        /// <summary>日志用的元素短名（跨文件复用，避免每个文件各写一份）。</summary>
        public static string DescribeElement(FrameworkElement? fe)
            => fe == null ? "<none>" : Describe(fe);

        /// <summary>
        /// 解析「这一处动效真正该动的元素」：
        ///   * 根本身 ≤ <paramref name="budgetPx"/> → 返回根（= 改前行为，小元素不受影响）；
        ///   * 否则在 <see cref="MotionHostMaxDepth"/> 层内找一个面积 ≤ 预算、且覆盖根 ≥
        ///     <see cref="MotionHostMinCoverage"/> 的内层内容容器（取最大的那个）；
        ///   * 都找不到 → 返回 null（调用方据此"该处不缓存也不做 scale"）。
        /// <paramref name="trace"/> 里带**全部候选与实际 px**，日志里可以直接核对。
        /// 只读 ActualWidth/ActualHeight + 走可视树，不改任何属性，节点数封顶。
        /// </summary>
        public static FrameworkElement? ResolveMotionHost(FrameworkElement? root, out double hostPx, out string trace)
        {
            hostPx = 0.0;
            trace = "root=null";
            if (root == null) return null;
            if (!SinkEnabled)
            {
                hostPx = DevicePixelArea(root);
                trace = "sink-disabled(TSURU_MOTION_SINK=0) root=" + Describe(root) + " px=" + (long)hostPx;
                return null;
            }

            double rootPx = DevicePixelArea(root);
            if (rootPx > 0 && rootPx <= MotionHostDevicePixelBudget)
            {
                hostPx = rootPx;
                trace = "root=" + Describe(root) + " px=" + (long)rootPx + " <=budget(no-sink)";
                return root;
            }

            // ── 解析结果缓存（同步预算 3ms 的关键）───────────────────────────────
            // 首页两棵根在多次切换之间尺寸一模一样，第 2 次起不该再走一遍可视树 + 建候选字符串。
            // 失效判据是根的 (ActualWidth, ActualHeight)：尺寸不变且宿主还在同一棵子树里就直接复用。
            double rootW = 0.0, rootH = 0.0;
            try { rootW = root.ActualWidth; rootH = root.ActualHeight; } catch { }
            if (_hostMemo.TryGetValue(root, out var memo) && memo.W == rootW && memo.H == rootH &&
                (memo.Host == null || ReferenceEquals(memo.Host, root) || IsInTree(memo.Host, root)))
            {
                hostPx = memo.HostPx;
                trace = "root=" + Describe(root) + " px=" + (long)rootPx + " memo-hit host=" +
                        DescribeElement(memo.Host) + " px=" + (long)memo.HostPx;
                return memo.Host;
            }

            double dpiScale = DpiScaleOf(root);
            FrameworkElement? best = null;
            double bestPx = 0.0;
            int visited = 0;
            var cands = new System.Text.StringBuilder();
            var level = new List<DependencyObject> { root };

            for (int depth = 1; depth <= MotionHostMaxDepth && level.Count > 0 && visited < MotionHostMaxNodes; depth++)
            {
                var next = new List<DependencyObject>();
                foreach (var node in level)
                {
                    int count;
                    try { count = VisualTreeHelper.GetChildrenCount(node); } catch { count = 0; }
                    for (int i = 0; i < count && visited < MotionHostMaxNodes; i++)
                    {
                        DependencyObject child;
                        try { child = VisualTreeHelper.GetChild(node, i); } catch { continue; }
                        visited++;
                        if (child is FrameworkElement fe)
                        {
                            double px = DevicePixelArea(fe, dpiScale);
                            if (cands.Length < 200) cands.Append(Describe(fe)).Append(':').Append((long)px).Append(' ');
                            // 只考虑**可见**的候选：把缓存挂到一个 Hidden/Collapsed 的子树上毫无意义
                            // （它根本不参与渲染），却会把真正可见的内容容器挤掉。
                            // 实验室页踩过这个坑：把工具浮层的收起态从 Collapsed 改成 Hidden 之后，
                            // 页面进场时的缓存下沉就错选中了浮层里的 ToolDetailPanel（同样接近 1.2MP），
                            // 真正可见的工具列表反而没缓存。
                            if (px > 0 && px <= MotionHostDevicePixelBudget && px > bestPx && fe.IsVisible)
                            {
                                best = fe;
                                bestPx = px;
                            }
                        }
                        if (depth < MotionHostMaxDepth) next.Add(child);
                    }
                }
                level = next;
            }

            double minPx = rootPx > 0 ? rootPx * MotionHostMinCoverage : 0.0;
            string head = "root=" + Describe(root) + " px=" + (long)rootPx +
                          " budget=" + (long)MotionHostDevicePixelBudget;
            if (best == null || bestPx < minPx)
            {
                // 解析失败才需要候选清单来解释"为什么降不下去"（成功路径不付这份字符串成本）
                trace = head + " cands=[" + cands.ToString().Trim() + "]" +
                        (best == null ? " -> host=none"
                                      : " -> best=" + Describe(best) + " px=" + (long)bestPx +
                                        " < coverage=" + (long)minPx + " -> host=none");
                // 失败结果**不进缓存**：可视树/布局可能还没成形（Frame 刚 Navigate 的那一帧就是），
                // 下一帧重试时要有机会重新解析。已经在缓存里的旧条目也一并清掉。
                try { _hostMemo.Remove(root); } catch { }
                return null;
            }

            hostPx = bestPx;
            trace = head + " -> host=" + Describe(best) + " px=" + (long)bestPx;
            Remember(root, rootW, rootH, best, bestPx);
            return best;
        }

        /// <summary>
        /// 解析「scale 该作用在哪个元素上」并写一行埋点。返回 null = 这一处不做 scale。
        /// 埋点形状：<c>[MotionPerf] scale-host &lt;元素&gt; px=&lt;实际&gt; budget=1200000 reason=&lt;...&gt; &lt;候选清单&gt;</c>
        /// </summary>
        public static FrameworkElement? ResolveScaleHost(FrameworkElement? root, string reason, out double hostPx)
        {
            var host = ResolveMotionHost(root, out hostPx, out string trace);
            // 解析成功不单独写日志 —— 紧随其后的 cache-on 那一行里已经带了
            // "host=<元素> hostPx=<px> root=<根> rootPx=<px> scale=sunk"，
            // 多写一行就多一次 File.AppendAllText，直接吃掉同步预算。
            if (host == null)
            {
                try { TsuruLauncher.Utilities.Logger.LogInfo("[MotionPerf] scale-host none reason=" + reason + " " + trace); }
                catch { }
            }
            return host;
        }

        private sealed class HostMemo
        {
            public double W, H, HostPx;
            public FrameworkElement? Host;
        }

        /// <summary>解析结果缓存（条数封顶，避免长期持有元素引用）。</summary>
        private static readonly Dictionary<FrameworkElement, HostMemo> _hostMemo =
            new Dictionary<FrameworkElement, HostMemo>();

        private static void Remember(FrameworkElement root, double w, double h, FrameworkElement? host, double px)
        {
            try
            {
                if (_hostMemo.Count > 24) _hostMemo.Clear();
                _hostMemo[root] = new HostMemo { W = w, H = h, Host = host, HostPx = px };
            }
            catch { }
        }

        /// <summary>宿主还在不在根这棵子树里（最多往上找 12 层，够用了）。</summary>
        private static bool IsInTree(DependencyObject? node, DependencyObject? root)
        {
            var cur = node;
            for (int i = 0; i < 12 && cur != null; i++)
            {
                if (ReferenceEquals(cur, root)) return true;
                try { cur = VisualTreeHelper.GetParent(cur); } catch { return false; }
            }
            return false;
        }

        /// <summary>「动效元素 → 实际挂上缓存的元素」映射（根太大时挂的是下沉后的内层容器）。</summary>
        private static readonly Dictionary<FrameworkElement, FrameworkElement> _motionCacheHosts =
            new Dictionary<FrameworkElement, FrameworkElement>();

        /// <summary>
        /// 给**已经解析好的宿主**挂动效缓存，并打出「实际挂上的元素与 px + 根的 px」。
        /// 埋点形状：<c>[MotionPerf] cache-target root=&lt;根&gt; px=&lt;根 px&gt; -&gt; host=&lt;实际&gt; px=&lt;宿主 px&gt; budget=1200000 reason=&lt;...&gt;</c>
        /// —— 这行就是"缓存不再被整页根 cache-skip、而是真的挂上了内层容器"的证据。
        /// </summary>
        public static FrameworkElement? AttachMotionCacheOn(FrameworkElement? root, FrameworkElement? host, double hostPx, string reason)
        {
            if (root == null || host == null) return null;
            if (!SinkEnabled) { DetachMotionCache(root); return null; }
            try
            {
                DetachMotionCache(root);

                // 埋点只留一行：把「实际挂上缓存的元素 + 它的 px + 是从哪个根下沉下来的 + 根的 px + 预算 + 原因」
                // 全部塞进 cache-on 那一行的 tag 里 —— 每多一行日志就是一次 File.AppendAllText，
                // 而这是同步阻塞预算（3ms）里最贵的东西，所以宁可一行写全，也不要两行。
                string tag = Describe(host) +
                             " root=" + Describe(root) + " rootPx=" + (long)DevicePixelArea(root) +
                             " hostPx=" + (long)hostPx + " budget=" + (long)MotionHostDevicePixelBudget +
                             " sink=" + (ReferenceEquals(host, root) ? "root" : "sunk") + " reason=" + reason;

                EnableBitmapCache(host, true, tag);
                if (host.CacheMode is not BitmapCache)
                {
                    TsuruLauncher.Utilities.Logger.LogInfo(
                        "[MotionPerf] cache-target none reason=" + reason + " refused host=" + Describe(host) +
                        " px=" + (long)hostPx);
                    return null;
                }
                if (!ReferenceEquals(host, root)) _motionCacheHosts[root] = host;
                return host;
            }
            catch { return null; }
        }

        /// <summary>
        /// 解析 + 挂动效缓存的一条龙入口（根 ≤ 预算就挂根，否则下沉到内层内容容器）。
        /// 谁都没挂上时记一笔带原因的 cache-skip 并返回 null。
        /// </summary>
        public static FrameworkElement? AttachMotionCache(FrameworkElement? root, string reason)
        {
            if (root == null) return null;
            if (!SinkEnabled) return null;
            if (GetCacheDisabled(root)) { DetachMotionCache(root); return null; }
            var host = ResolveMotionHost(root, out double px, out string trace);
            if (host != null) return AttachMotionCacheOn(root, host, px, reason);

            try
            {
                TsuruLauncher.Utilities.Logger.LogInfo("[MotionPerf] cache-target none reason=" + reason + " " + trace);
                MotionPerf.NoteCacheSkipped(Describe(root) + " reason=" + reason + " host=none",
                    DevicePixelArea(root), MotionHostDevicePixelBudget);
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 布局还没跑（<c>ActualWidth/ActualHeight = 0</c>，例如 Frame 刚 Navigate、页根还没 Measure）时用：
        /// 投递一个 <see cref="System.Windows.Threading.DispatcherPriority.Render"/> 回调，
        /// 等这一帧的布局完成后解析内层容器再把动效缓存挂上。
        /// <paramref name="stillWanted"/> 返回 false（动画已经落终态）就什么都不做 —— 绝不留下永久缓存。
        /// 只投递回调、绝不占用调用方的同步时间（点击预算 3ms）。
        /// </summary>
        public static void AttachMotionCacheAfterLayout(FrameworkElement? root, string reason,
            Func<bool>? stillWanted = null, int attempts = 4)
        {
            if (root == null) return;
            var dispatcher = root.Dispatcher;
            if (dispatcher == null) return;
            int left = Math.Max(1, attempts);
            Action? step = null;
            step = () =>
            {
                try
                {
                    if (stillWanted != null && !stillWanted()) return;      // 动画已落终态：不挂，也不留永久缓存
                    var host = ResolveMotionHost(root, out double px, out string trace);
                    if (host == null && --left > 0)
                    {
                        // 这一帧可视树/布局还没成形（Frame 刚 Navigate）：下一帧再试，中间不写日志
                        try { dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, step!); } catch { }
                        return;
                    }
                    if (host != null)
                    {
                        AttachMotionCacheOn(root, host, px, reason);
                        return;
                    }
                    TsuruLauncher.Utilities.Logger.LogInfo("[MotionPerf] cache-target none reason=" + reason + " " + trace);
                    MotionPerf.NoteCacheSkipped(Describe(root) + " reason=" + reason + " host=none",
                        DevicePixelArea(root), MotionHostDevicePixelBudget);
                }
                catch { }
            };
            try { dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, step!); }
            catch { }
        }

        /// <summary>摘掉 <see cref="AttachMotionCache"/> / <see cref="AttachMotionCacheOn"/> 挂上的缓存（根 + 下沉后的宿主一起摘）。</summary>
        public static void DetachMotionCache(FrameworkElement? root)
        {
            if (root == null) return;
            try
            {
                if (_motionCacheHosts.TryGetValue(root, out var host))
                {
                    _motionCacheHosts.Remove(root);
                    if (host != null && !ReferenceEquals(host, root)) EnableBitmapCache(host, false);
                }
                EnableBitmapCache(root, false);
            }
            catch { }
        }

        #endregion

        #region Prewarm（切换前预热另一棵子树）

        /// <summary>
        /// 预热一个（通常是 Collapsed 的）元素的布局：临时 Visible -&gt; UpdateLayout -&gt; 还原 Visibility。
        /// 整段在**一个 Dispatcher 回调内**跑完，中间不会插入渲染，所以用户看不到闪帧
        /// （渲染只在 UI 线程的空闲点发生，回调是同步执行完的）。
        ///
        /// 为什么需要：两态内容（主页的极简根 / 信息根）互相切换时，目标那棵是 Collapsed，
        /// 第一次 Visible 会作废布局、整棵子树重新 measure/arrange；这一步如果落在切换动画的
        /// 第一帧里，就是那一帧的掉帧来源。调用方可以在切换之前先 <see cref="Prewarm"/> 目标元素。
        ///
        /// 同步阻塞预算 3ms：measure/arrange 不能占点击回调，所以这里只**投递**一个
        /// Render 优先级的 Dispatcher 回调（下一个渲染帧之前执行）后立刻返回。
        /// 只碰 Visibility（且在同一次回调内还原）与布局，不动 Opacity / RenderTransform / Width。
        /// </summary>
        public static void Prewarm(FrameworkElement? element)
        {
            if (element == null) return;
            var dispatcher = element.Dispatcher;
            if (dispatcher == null) return;

            try
            {
                dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render,
                    new Action(() => PrewarmCore(element)));
            }
            catch { }
        }

        private static void PrewarmCore(FrameworkElement element)
        {
            double t0 = MotionPerf.NowMs;
            string tag = Describe(element);

            // Visibility 取**执行时刻**的值，不取投递时刻的：
            // 投递到执行之间调用方可能已经把元素切成 Visible（切换动画已经开跑），
            // 那种情况下绝不能把它还原回 Collapsed。
            var before = element.Visibility;
            bool flipped = false;

            try
            {
                if (before != Visibility.Visible)
                {
                    element.Visibility = Visibility.Visible;
                    flipped = true;
                }
                element.UpdateLayout();            // 同步跑完 measure/arrange
            }
            catch { }
            finally
            {
                if (flipped)
                {
                    try { element.Visibility = before; } catch { }
                }
            }

            try
            {
                TsuruLauncher.Utilities.Logger.LogInfo(
                    $"[MotionPerf] prewarm {tag} took={MotionPerf.NowMs - t0:0.00}ms " +
                    $"vis={(flipped ? before + "->Visible->" + before : "already-visible")}");
            }
            catch { }
        }

        #endregion

        private static string Describe(FrameworkElement fe)
            => string.IsNullOrEmpty(fe.Name) ? fe.GetType().Name : fe.GetType().Name + "#" + fe.Name;

        #endregion

        #region HoverTint（卡片 hover 的颜色过渡）

        // 为什么不用动画画刷的 Color：
        // 主题画刷来自 DynamicResource（见 ThemeService / ThemeBrush），是共享且冻结的
        // SolidColorBrush，WPF 不允许对冻结画刷做 Color 动画；直接换成本地非冻结副本
        // 又会盖掉 DynamicResource，主题切换时这些卡片不再跟随。
        // 所以这里改成「在 AdornerLayer 上叠一层同圆角的色块，动画它的 Opacity」——
        // 效果等价于 Axolotl 的 transition-[background-color] 250ms ease-in-out
        // （ButtonStyled.vue:267-271 / InstanceRowCard.vue:15），
        // 既保留 DynamicResource，又能真正做出 250ms 的平滑 hover。

        /// <summary>hover 时叠加的色块画刷（建议给 Surface4/5Brush 之类的浅色令牌）。</summary>
        public static readonly DependencyProperty HoverTintBrushProperty =
            DependencyProperty.RegisterAttached("HoverTintBrush", typeof(Brush), typeof(MotionAssist),
                new PropertyMetadata(null, OnHoverTintChanged));

        public static Brush? GetHoverTintBrush(DependencyObject o) => (Brush?)o.GetValue(HoverTintBrushProperty);
        public static void SetHoverTintBrush(DependencyObject o, Brush? v) => o.SetValue(HoverTintBrushProperty, v);

        /// <summary>过渡时长，默认 250ms（Axolotl background-color 0.25s ease-in-out）。</summary>
        public static readonly DependencyProperty HoverTintMsProperty =
            DependencyProperty.RegisterAttached("HoverTintMs", typeof(double), typeof(MotionAssist),
                new PropertyMetadata(AxolotlMotion.ButtonColorMs));

        public static double GetHoverTintMs(DependencyObject o) => (double)o.GetValue(HoverTintMsProperty);
        public static void SetHoverTintMs(DependencyObject o, double v) => o.SetValue(HoverTintMsProperty, v);

        /// <summary>色块终态不透明度（默认 0.6，做出「轻微提亮」而不是整块换色）。</summary>
        public static readonly DependencyProperty HoverTintOpacityProperty =
            DependencyProperty.RegisterAttached("HoverTintOpacity", typeof(double), typeof(MotionAssist),
                new PropertyMetadata(0.6));

        public static double GetHoverTintOpacity(DependencyObject o) => (double)o.GetValue(HoverTintOpacityProperty);
        public static void SetHoverTintOpacity(DependencyObject o, double v) => o.SetValue(HoverTintOpacityProperty, v);

        /// <summary>圆角半径；不设时自动取 Border.CornerRadius.TopLeft。</summary>
        public static readonly DependencyProperty HoverTintRadiusProperty =
            DependencyProperty.RegisterAttached("HoverTintRadius", typeof(double), typeof(MotionAssist),
                new PropertyMetadata(double.NaN));

        public static double GetHoverTintRadius(DependencyObject o) => (double)o.GetValue(HoverTintRadiusProperty);
        public static void SetHoverTintRadius(DependencyObject o, double v) => o.SetValue(HoverTintRadiusProperty, v);

        private static bool _hoverTintLayerWarned;

        private static readonly DependencyProperty HoverTintAdornerProperty =
            DependencyProperty.RegisterAttached("HoverTintAdorner", typeof(HoverTintAdorner), typeof(MotionAssist),
                new PropertyMetadata(null));

        private static void OnHoverTintChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement fe) return;

            fe.MouseEnter -= OnTintMouseEnter;
            fe.MouseLeave -= OnTintMouseLeave;
            fe.Unloaded -= OnTintUnloaded;

            if (e.NewValue is not Brush) return;

            fe.MouseEnter += OnTintMouseEnter;
            fe.MouseLeave += OnTintMouseLeave;
            fe.Unloaded += OnTintUnloaded;

            if (fe.IsMouseOver) ApplyHoverTint(fe, true, animate: false);
        }

        /// <summary>
        /// 元素离开可视树时把 hover 色块从 AdornerLayer 上摘掉。
        /// 不摘的话，翻页 / 列表虚拟化回收之后 AdornerLayer 里会累积一堆
        /// "被装饰元素已不在树上"的色块，每一帧 Arrange 都要过一遍 —— 这是另一种「永久缓存」。
        /// </summary>
        private static void OnTintUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.GetValue(HoverTintAdornerProperty) is not HoverTintAdorner adorner) return;

            fe.ClearValue(HoverTintAdornerProperty);
            try { adorner.OwnerLayer?.Remove(adorner); } catch { }
            adorner.OwnerLayer = null;
        }

        private static void OnTintMouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement fe) ApplyHoverTint(fe, true, animate: true);
        }

        private static void OnTintMouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is FrameworkElement fe) ApplyHoverTint(fe, false, animate: true);
        }

        private static void ApplyHoverTint(FrameworkElement fe, bool hover, bool animate)
        {
            var adorner = (HoverTintAdorner?)fe.GetValue(HoverTintAdornerProperty);
            if (adorner == null)
            {
                if (!hover) return;
                var layer = AdornerLayer.GetAdornerLayer(fe);
                var brush = GetHoverTintBrush(fe);
                if (layer == null || brush == null)
                {
                    if (layer == null && !_hoverTintLayerWarned)
                    {
                        _hoverTintLayerWarned = true;
                        try
                        {
                            TsuruLauncher.Utilities.Logger.LogInfo(
                                "[Motion] hover tint: no AdornerLayer above " + fe.GetType().Name +
                                " -> hover color transition skipped");
                        }
                        catch { }
                    }
                    return;
                }

                double radius = GetHoverTintRadius(fe);
                if (double.IsNaN(radius))
                    radius = fe is Border b ? b.CornerRadius.TopLeft : 12.0;

                adorner = new HoverTintAdorner(fe, brush, radius);
                fe.SetValue(HoverTintAdornerProperty, adorner);
                try { layer.Add(adorner); } catch { return; }
                adorner.OwnerLayer = layer;
            }

            double to = hover ? GetHoverTintOpacity(fe) : 0.0;
            double ms = GetHoverTintMs(fe);

            if (!animate || !AxolotlMotion.FullMotionEnabled)
            {
                adorner.BeginAnimation(HoverTintAdorner.TintOpacityProperty, null);
                adorner.TintOpacity = to;
                return;
            }

            var anim = new DoubleAnimation(to, AxolotlMotion.Ms(ms)) { EasingFunction = AxolotlMotion.EaseInOut };
            adorner.BeginAnimation(HoverTintAdorner.TintOpacityProperty, anim);
        }

        /// <summary>AdornerLayer 上的圆角色块，只画、不参与命中测试、不影响布局。</summary>
        private sealed class HoverTintAdorner : Adorner
        {
            public static readonly DependencyProperty TintOpacityProperty =
                DependencyProperty.Register(nameof(TintOpacity), typeof(double), typeof(HoverTintAdorner),
                    new PropertyMetadata(0.0, OnTintOpacityChanged));

            public double TintOpacity
            {
                get => (double)GetValue(TintOpacityProperty);
                set => SetValue(TintOpacityProperty, value);
            }

            private static void OnTintOpacityChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
                => ((HoverTintAdorner)d).InvalidateVisual();

            private readonly Brush _brush;
            private readonly double _radius;

            /// <summary>加入时记住宿主层，元素 Unloaded 时才能精确摘除。</summary>
            public AdornerLayer? OwnerLayer;

            public HoverTintAdorner(UIElement adorned, Brush brush, double radius) : base(adorned)
            {
                _brush = brush;
                _radius = radius;
                IsHitTestVisible = false;
            }

            protected override void OnRender(DrawingContext drawingContext)
            {
                double o = TintOpacity;
                if (o <= 0.002) return;

                var size = AdornedElement.RenderSize;
                if (size.Width <= 0.5 || size.Height <= 0.5) return;

                double r = Math.Max(0, Math.Min(_radius, Math.Min(size.Width, size.Height) / 2.0));
                drawingContext.PushOpacity(o);
                drawingContext.DrawRoundedRectangle(_brush, null, new Rect(size), r, r);
                drawingContext.Pop();
            }
        }

        #endregion

        #region Wiring

        private static void OnMotionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement fe) return;

            var state = GetState(fe);
            state.HoverScale = GetHoverScale(fe);
            state.PressScale = GetPressScale(fe);

            bool needMouse = state.HoverScale > 0.0 || GetRevealOnHover(fe);
            bool needPress = state.PressScale > 0.0;

            fe.MouseEnter -= OnMouseEnter;
            fe.MouseLeave -= OnMouseLeave;
            fe.PreviewMouseLeftButtonDown -= OnPreviewMouseDown;
            fe.PreviewMouseLeftButtonUp -= OnPreviewMouseUp;
            fe.LostMouseCapture -= OnLostMouseCapture;

            if (needMouse)
            {
                fe.MouseEnter += OnMouseEnter;
                fe.MouseLeave += OnMouseLeave;
            }

            if (needPress)
            {
                fe.PreviewMouseLeftButtonDown += OnPreviewMouseDown;
                fe.PreviewMouseLeftButtonUp += OnPreviewMouseUp;
                fe.LostMouseCapture += OnLostMouseCapture;
            }

            // 入场初始态：RevealOnHover 的元素一开始是「藏起来」的
            if (GetRevealOnHover(fe) && !state.RevealInitialised)
            {
                state.RevealInitialised = true;
                state.Hover = fe.IsMouseOver;
                ApplyReveal(fe, state, animate: false);
            }
            else if (state.HoverScale > 0.0)
            {
                state.Hover = fe.IsMouseOver;
                ApplyScale(fe, state, animate: false);
            }
        }

        private static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached("State", typeof(MotionState), typeof(MotionAssist),
                new PropertyMetadata(null));

        private static MotionState GetState(FrameworkElement fe)
        {
            if (fe.GetValue(StateProperty) is MotionState s) return s;
            s = new MotionState();
            fe.SetValue(StateProperty, s);
            return s;
        }

        private static void OnMouseEnter(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var s = GetState(fe);
            s.Hover = true;
            ApplyScale(fe, s, true);
            ApplyRevealToDescendants(fe, s, true);
        }

        private static void OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var s = GetState(fe);
            s.Hover = false;
            s.Pressed = false;
            ApplyScale(fe, s, true);
            ApplyRevealToDescendants(fe, s, false);
        }

        private static void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            if (fe.IsEnabled == false) return;
            var s = GetState(fe);
            s.Pressed = true;
            ApplyScale(fe, s, true);
        }

        private static void OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var s = GetState(fe);
            if (!s.Pressed) return;
            s.Pressed = false;
            ApplyScale(fe, s, true);
        }

        private static void OnLostMouseCapture(object sender, MouseEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;
            var s = GetState(fe);
            if (!s.Pressed) return;
            s.Pressed = false;
            ApplyScale(fe, s, true);
        }

        private static void ApplyScale(FrameworkElement fe, MotionState s, bool animate)
        {
            double target = s.Pressed && s.PressScale > 0.0
                ? s.PressScale
                : (s.Hover && s.HoverScale > 0.0 ? s.HoverScale : 1.0);

            double ms = s.Pressed ? GetPressScaleMs(fe) : GetHoverScaleMs(fe);
            IEasingFunction ease = ToEase(s.Pressed ? GetPressScaleEase(fe) : GetHoverScaleEase(fe));

            var scale = MotionVisuals.EnsureScale(fe);
            if (!animate || !AxolotlMotion.FullMotionEnabled)
            {
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                scale.ScaleX = target;
                scale.ScaleY = target;
                return;
            }

            // 【同 PageTransition.PlayMotion 的根因修复】Storyboard.SetTarget 指向 ScaleTransform
            // （Freezable）时动画不生效 —— 改成对 Transform 直接 BeginAnimation。
            // 默认 FillBehavior.HoldEnd 会把缩放保持在终态，所以不需要额外的 Completed 归位。
            var anim = new DoubleAnimation(target, AxolotlMotion.Ms(ms)) { EasingFunction = ease };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        private static void ApplyReveal(FrameworkElement fe, MotionState s, bool animate)
        {
            bool visible = s.Hover;
            double ms = GetRevealMs(fe);
            double scaleTo = visible ? 1.0 : GetRevealScale(fe);
            double offsetTo = visible ? 0.0 : GetRevealOffsetY(fe);
            double opacityTo = visible ? 1.0 : 0.0;

            var scale = MotionVisuals.EnsureScale(fe);
            var translate = MotionVisuals.EnsureTranslate(fe);

            if (!animate || !AxolotlMotion.FullMotionEnabled)
            {
                fe.BeginAnimation(UIElement.OpacityProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                fe.Opacity = opacityTo;
                scale.ScaleX = scaleTo; scale.ScaleY = scaleTo;
                translate.Y = offsetTo;
                return;
            }

            // 【同 PageTransition.PlayMotion 的根因修复】不再走 Storyboard.SetTarget(Freezable)：
            // Opacity 用 Storyboard（目标是元素本身，可用），Transform 直接 BeginAnimation。
            var fade = new DoubleAnimation(opacityTo, AxolotlMotion.Ms(ms)) { EasingFunction = AxolotlMotion.EaseInOut };
            Storyboard.SetTarget(fade, fe);
            Storyboard.SetTargetProperty(fade, new PropertyPath(UIElement.OpacityProperty));
            var sb = new Storyboard();
            sb.Children.Add(fade);
            sb.Begin();

            var zoom = new DoubleAnimation(scaleTo, AxolotlMotion.Ms(ms)) { EasingFunction = AxolotlMotion.EaseInOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, zoom);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, zoom);

            if (Math.Abs(offsetTo - translate.Y) > 0.01)
            {
                var move = new DoubleAnimation(offsetTo, AxolotlMotion.Ms(ms)) { EasingFunction = AxolotlMotion.EaseInOut };
                translate.BeginAnimation(TranslateTransform.YProperty, move);
            }
        }

        /// <summary>
        /// hover 父级时，把 <c>RevealOnHover</c> 的子孙一起显隐
        /// （对应 Axolotl 的 <c>group-hover:</c> 语义）。
        /// </summary>
        private static void ApplyRevealToDescendants(DependencyObject root, MotionState s, bool hover)
        {
            int count = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is FrameworkElement fe && GetRevealOnHover(fe))
                {
                    var cs = GetState(fe);
                    cs.Hover = hover;
                    ApplyReveal(fe, cs, true);
                }
                ApplyRevealToDescendants(child, s, hover);
            }
        }

        private static IEasingFunction ToEase(MotionEase ease) => ease switch
        {
            MotionEase.Ease => AxolotlMotion.Ease,
            MotionEase.EaseOut => AxolotlMotion.EaseOut,
            MotionEase.EaseIn => AxolotlMotion.EaseIn,
            MotionEase.Overshoot => AxolotlMotion.OvershootEase,
            MotionEase.PopupIn => AxolotlMotion.PopupInEase,
            MotionEase.PopupOut => AxolotlMotion.PopupOutEase,
            MotionEase.Sidebar => AxolotlMotion.SidebarEase,
            _ => AxolotlMotion.EaseInOut,
        };

        private sealed class MotionState
        {
            public bool Hover;
            public bool Pressed;
            public bool RevealInitialised;
            public double HoverScale;
            public double PressScale;
        }

        #endregion
    }

    /// <summary>RenderTransform 的共享获取逻辑：已有变换会被包进 TransformGroup 保留，绝不覆盖。</summary>
    internal static class MotionVisuals
    {
        public static ScaleTransform EnsureScale(FrameworkElement element)
        {
            if (element.RenderTransform is ScaleTransform direct && element.RenderTransform is not TransformGroup)
                return direct;

            var group = EnsureGroup(element);
            foreach (var child in group.Children)
            {
                if (child is ScaleTransform existing) return existing;
            }
            var added = new ScaleTransform(1.0, 1.0);
            group.Children.Add(added);
            EnsureOrigin(element);
            return added;
        }

        public static TranslateTransform EnsureTranslate(FrameworkElement element)
        {
            if (element.RenderTransform is TranslateTransform direct)
                return direct;

            var group = EnsureGroup(element);
            foreach (var child in group.Children)
            {
                if (child is TranslateTransform existing) return existing;
            }
            var added = new TranslateTransform();
            group.Children.Add(added);
            return added;
        }

        private static TransformGroup EnsureGroup(FrameworkElement element)
        {
            if (element.RenderTransform is TransformGroup existingGroup) return existingGroup;

            var previous = element.RenderTransform;
            var wrapper = new TransformGroup();
            element.RenderTransform = null;
            if (previous != null && !ReferenceEquals(previous, Transform.Identity))
            {
                try { wrapper.Children.Add(previous); } catch { /* 冻结或已被引用：放弃复用 */ }
            }
            element.RenderTransform = wrapper;
            return wrapper;
        }

        private static void EnsureOrigin(FrameworkElement element)
        {
            if (element.RenderTransformOrigin == new Point(0.5, 0.5)) return;
            element.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        /// <summary>（保留）在 XAML 之外手工挂 BitmapCache 的入口。</summary>
        public static void Cache(FrameworkElement element) => MotionAssist.EnableBitmapCache(element, true);
    }
}
