using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TsuruLauncher.Services.Animation;

namespace TsuruLauncher.Controls
{
    /// <summary>指示块参与动画的轴向。</summary>
    public enum SegmentAxis
    {
        /// <summary>单行横向分段（默认）：只动 <c>TranslateX</c>（+ 自身 Width）。</summary>
        Horizontal = 0,

        /// <summary>纵向列表（左导航 / 版本设置标签栏）：只动 <c>TranslateY</c>（+ 自身 Height）。</summary>
        Vertical = 1,

        /// <summary>会换行的 chips（WrapPanel）：<c>TranslateX + TranslateY</c> 一起动，换行后仍然直达目标项。</summary>
        Both = 2,
    }

    /// <summary>
    /// <b>分段胶囊的滑动指示块（可复用的一套）</b> —— Axolotl <c>NavRail.vue:104-115</c> /
    /// <c>Tabs.vue</c> / <c>FilterPills.vue</c> 那种「选中底色是一块会滑过去的胶囊」的 WPF 落地。
    ///
    /// 改前的根因：每个分段项在 <c>IsChecked</c> 触发器里直接换 <c>Background</c> ——
    /// 主题画刷是 DynamicResource 共享画刷，WPF 不允许对冻结画刷做 Color 动画，
    /// 于是选中态只能「瞬时硬切」（见 AXOLOTL_MOTION_PARAMS.md 已知取舍 1）。
    /// 现在把选中底色从「每一项自己画」改成「容器里一块绝对定位的胶囊滑过去」。
    ///
    /// <para><b>用法</b>（给容器加两个属性 + 一个 Canvas 里的指示块）：</para>
    /// <code>
    /// &lt;Grid motion:SegmentedIndicator.Host="True"
    ///       motion:SegmentedIndicator.Indicator="{Binding ElementName=FilterPill}"&gt;
    ///     &lt;Canvas IsHitTestVisible="False"&gt;
    ///         &lt;Border x:Name="FilterPill" Opacity="0" CornerRadius="14"
    ///                 Background="{DynamicResource AccentBrush}"/&gt;
    ///     &lt;/Canvas&gt;
    ///     &lt;StackPanel Orientation="Horizontal"&gt;
    ///         &lt;RadioButton GroupName="G" Content="甲"/&gt;
    ///         &lt;RadioButton GroupName="G" Content="乙"/&gt;
    ///     &lt;/StackPanel&gt;
    /// &lt;/Grid&gt;
    /// </code>
    ///
    /// <para><b>为什么必须放在 <see cref="Canvas"/> 里</b>：<see cref="Canvas"/> 的
    /// <c>MeasureOverride</c> 恒返回 (0,0)，指示块的 Width/Height 动画<b>永远不会</b>改变宿主的
    /// desired size —— 也就不会触发 <c>SizeChanged → 自我打断</c> 的反馈环（NavSlider 当初抖动的原因）。
    /// 指示块是 <c>HorizontalAlignment=Left / VerticalAlignment=Top</c> 的绝对定位元素，
    /// 位置由 <c>Canvas.Left/Top</c>（或 Grid 下的 Margin）承担「新格子」，位移只由
    /// <b>一个 TranslateTransform</b> 承担 —— 没有 BeginTime 错峰、没有 ScaleY 拉伸补偿、
    /// 没有「延迟属性 + 缩放补偿」那套。</para>
    ///
    /// <para><b>连续性</b>：每次都先读出<b>动画中的当前值</b>，再清时钟、写新基线，令
    /// <c>起点 = 当前动画值 + 上一次目标 − 新目标</c>，所以连点 / 反向点都不跳变。</para>
    ///
    /// <para>时长与曲线固定复用 <see cref="AxolotlMotion.NavSliderMs"/>（150ms）与
    /// <see cref="AxolotlMotion.EaseInOut"/>（cubic-bezier(0.4, 0, 0.2, 1)）。</para>
    /// </summary>
    public static class SegmentedIndicator
    {
        #region 附加属性

        /// <summary>在<b>容器</b>上打开指示块联动（自动扫描子孙 RadioButton）。</summary>
        public static readonly DependencyProperty HostProperty =
            DependencyProperty.RegisterAttached("Host", typeof(bool), typeof(SegmentedIndicator),
                new PropertyMetadata(false, OnWiringChanged));

        public static bool GetHost(DependencyObject o) => (bool)o.GetValue(HostProperty);
        public static void SetHost(DependencyObject o, bool v) => o.SetValue(HostProperty, v);

        /// <summary>指示块元素（一般是 Canvas 里的一个 Border）。</summary>
        public static readonly DependencyProperty IndicatorProperty =
            DependencyProperty.RegisterAttached("Indicator", typeof(FrameworkElement), typeof(SegmentedIndicator),
                new PropertyMetadata(null, OnWiringChanged));

        public static FrameworkElement? GetIndicator(DependencyObject o) => (FrameworkElement?)o.GetValue(IndicatorProperty);
        public static void SetIndicator(DependencyObject o, FrameworkElement? v) => o.SetValue(IndicatorProperty, v);

        /// <summary>移动轴向，默认单行横向。</summary>
        public static readonly DependencyProperty AxisProperty =
            DependencyProperty.RegisterAttached("Axis", typeof(SegmentAxis), typeof(SegmentedIndicator),
                new PropertyMetadata(SegmentAxis.Horizontal, OnWiringChanged));

        public static SegmentAxis GetAxis(DependencyObject o) => (SegmentAxis)o.GetValue(AxisProperty);
        public static void SetAxis(DependencyObject o, SegmentAxis v) => o.SetValue(AxisProperty, v);

        /// <summary>时长（ms），默认 150（<see cref="AxolotlMotion.NavSliderMs"/>）。</summary>
        public static readonly DependencyProperty MsProperty =
            DependencyProperty.RegisterAttached("Ms", typeof(double), typeof(SegmentedIndicator),
                new PropertyMetadata(AxolotlMotion.NavSliderMs));

        public static double GetMs(DependencyObject o) => (double)o.GetValue(MsProperty);
        public static void SetMs(DependencyObject o, double v) => o.SetValue(MsProperty, v);

        private static readonly DependencyProperty StateProperty =
            DependencyProperty.RegisterAttached("State", typeof(SegmentState), typeof(SegmentedIndicator),
                new PropertyMetadata(null));

        private static SegmentState? GetState(DependencyObject o) => (SegmentState?)o.GetValue(StateProperty);
        private static void SetState(DependencyObject o, SegmentState? s) => o.SetValue(StateProperty, s);

        /// <summary>item -&gt; 所属 state 的反查（让每个项的 SizeChanged 能找回宿主）。</summary>
        private static readonly DependencyProperty OwnerProperty =
            DependencyProperty.RegisterAttached("Owner", typeof(SegmentState), typeof(SegmentedIndicator),
                new PropertyMetadata(null));

        #endregion

        #region 状态

        private sealed class SegmentState
        {
            public FrameworkElement Host = null!;
            public FrameworkElement? Indicator;
            public TranslateTransform? Translate;
            public SegmentAxis Axis = SegmentAxis.Horizontal;
            public double Ms = AxolotlMotion.NavSliderMs;

            /// <summary>当前挂在宿主子树里的分段项（已挂 SizeChanged）。</summary>
            public readonly List<RadioButton> Items = new();

            /// <summary>指示块是否已经定过位（决定首帧是「落终态」还是「淡入」）。</summary>
            public bool Positioned;

            /// <summary>位移动画正在播（期间的被动布局刷新一律不打断）。</summary>
            public bool Animating;

            /// <summary>已经淡入显示过（避免每次 Update 都重播淡入）。</summary>
            public bool Shown;

            /// <summary>上一次写下的基线（宿主坐标系），用于算「视觉上现在在哪」。</summary>
            public double TargetX, TargetY;

            /// <summary>Scan 合并标记（Loaded 会成串触发，避免 O(n²) 的重复扫描）。</summary>
            public bool ScanPending;
        }

        #endregion

        #region 装配 / 拆卸

        private static void OnWiringChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not FrameworkElement host) return;
            Detach(host);
            if (GetHost(host)) Attach(host);
        }

        private static void Attach(FrameworkElement host)
        {
            host.Loaded += OnHostLoaded;
            host.Unloaded += OnHostUnloaded;
            if (host.IsLoaded) Materialize(host);   // 属性是在 Loaded 之后才设的（例如模板/绑定）
        }

        private static void Detach(FrameworkElement host)
        {
            host.Loaded -= OnHostLoaded;
            host.Unloaded -= OnHostUnloaded;
            Dematerialize(host);
        }

        private static void OnHostLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement host) Materialize(host);
        }

        private static void OnHostUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement host) Dematerialize(host);
        }

        /// <summary>宿主进入可视树：解析指示块、挂事件、首帧直接落终态（不播动画）。</summary>
        private static void Materialize(FrameworkElement host)
        {
            if (!GetHost(host)) return;
            var ind = GetIndicator(host);
            if (ind == null) return;                     // 还没指定指示块：等属性回调再来
            if (GetState(host) != null) return;          // 已经装配过

            var st = new SegmentState
            {
                Host = host,
                Indicator = ind,
                Axis = GetAxis(host),
                Ms = GetMs(host) > 0 ? GetMs(host) : AxolotlMotion.NavSliderMs,
            };

            // 绝对定位 + 不参与命中测试：指示块是「背景层」，永远不吃点击。
            ind.IsHitTestVisible = false;
            ind.HorizontalAlignment = HorizontalAlignment.Left;
            ind.VerticalAlignment = VerticalAlignment.Top;
            ind.RenderTransformOrigin = new Point(0, 0);
            st.Translate = EnsureTranslate(ind);

            SetState(host, st);

            host.SizeChanged += OnHostSizeChanged;
            host.IsVisibleChanged += OnHostVisibleChanged;
            // 子孙的 Loaded/Unloaded：ItemsControl 延迟realize / 虚拟化回收都能被看见
            host.AddHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(OnDescendantLoaded), true);
            host.AddHandler(FrameworkElement.UnloadedEvent, new RoutedEventHandler(OnDescendantUnloaded), true);
            // 子孙里任意一个 RadioButton 被选中：ToggleButton.Checked 是冒泡路由事件，一个 handler 全接住
            host.AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnAnyChecked), true);

            Scan(st);
            UpdateCore(st, animate: false, reason: "load");
        }

        private static void Dematerialize(FrameworkElement host)
        {
            var st = GetState(host);
            if (st == null) return;

            host.SizeChanged -= OnHostSizeChanged;
            host.IsVisibleChanged -= OnHostVisibleChanged;
            host.RemoveHandler(FrameworkElement.LoadedEvent, new RoutedEventHandler(OnDescendantLoaded));
            host.RemoveHandler(FrameworkElement.UnloadedEvent, new RoutedEventHandler(OnDescendantUnloaded));
            host.RemoveHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(OnAnyChecked));

            foreach (var item in st.Items)
            {
                item.SizeChanged -= OnItemSizeChanged;
                item.ClearValue(OwnerProperty);
            }
            st.Items.Clear();

            if (st.Indicator != null)
            {
                var tr = EnsureTranslate(st.Indicator);
                tr.BeginAnimation(TranslateTransform.XProperty, null);
                tr.BeginAnimation(TranslateTransform.YProperty, null);
                tr.X = 0; tr.Y = 0;
                st.Indicator.BeginAnimation(FrameworkElement.WidthProperty, null);
                st.Indicator.BeginAnimation(FrameworkElement.HeightProperty, null);
                st.Indicator.BeginAnimation(UIElement.OpacityProperty, null);
                PageTransition.SettleElement(st.Indicator, resetOpacity: false);
            }

            SetState(host, null);
        }

        /// <summary>取出/建立指示块的 TranslateTransform（已有则复用，不覆盖作者写的变换）。</summary>
        private static TranslateTransform EnsureTranslate(FrameworkElement ind)
        {
            if (ind.RenderTransform is TranslateTransform direct) return direct;

            if (ind.RenderTransform is TransformGroup group)
            {
                foreach (var child in group.Children)
                    if (child is TranslateTransform existing) return existing;
                var added = new TranslateTransform();
                group.Children.Add(added);
                return added;
            }

            if (ind.RenderTransform == null || ind.RenderTransform.Value.IsIdentity)
            {
                var t = new TranslateTransform();
                ind.RenderTransform = t;
                return t;
            }

            // 作者自己写了别的变换：包一层 TransformGroup，绝不丢掉它
            var wrapper = new TransformGroup();
            var previous = ind.RenderTransform;
            ind.RenderTransform = null;
            wrapper.Children.Add(previous);
            var own = new TranslateTransform();
            wrapper.Children.Add(own);
            ind.RenderTransform = wrapper;
            return own;
        }

        #endregion

        #region 扫描分段项

        private static void ScheduleScan(SegmentState st)
        {
            if (st.ScanPending) return;
            st.ScanPending = true;
            st.Host.Dispatcher.BeginInvoke(new Action(() =>
            {
                st.ScanPending = false;
                if (GetState(st.Host) != st) return;
                if (Scan(st)) UpdateCore(st, animate: false, reason: "items");
            }), DispatcherPriority.Loaded);
        }

        /// <summary>把宿主子树里的 RadioButton 收进来（嵌套的 SegmentedIndicator 宿主交给自己管）。</summary>
        private static bool Scan(SegmentState st)
        {
            var found = new List<RadioButton>(16);
            Collect(st.Host, st.Host, found);

            bool changed = found.Count != st.Items.Count;
            if (!changed)
            {
                for (int i = 0; i < found.Count; i++)
                    if (!ReferenceEquals(found[i], st.Items[i])) { changed = true; break; }
            }
            if (!changed) return false;

            foreach (var item in st.Items)
            {
                item.SizeChanged -= OnItemSizeChanged;
                item.ClearValue(OwnerProperty);
            }
            st.Items.Clear();

            foreach (var item in found)
            {
                st.Items.Add(item);
                item.SetValue(OwnerProperty, st);
                item.SizeChanged += OnItemSizeChanged;
            }
            return true;
        }

        private static void Collect(DependencyObject node, FrameworkElement host, List<RadioButton> into)
        {
            int count = 0;
            try { count = VisualTreeHelper.GetChildrenCount(node); } catch { return; }
            for (int i = 0; i < count; i++)
            {
                DependencyObject child;
                try { child = VisualTreeHelper.GetChild(node, i); } catch { continue; }

                if (child is RadioButton rb)
                {
                    into.Add(rb);
                    continue;   // 分段项内部不会再嵌分段项
                }
                if (child is FrameworkElement fe && !ReferenceEquals(fe, host) && GetHost(fe))
                    continue;   // 嵌套分段组：由它自己管

                Collect(child, host, into);
            }
        }

        #endregion

        #region 事件

        private static void OnDescendantLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement host) return;
            var st = GetState(host);
            if (st == null) return;
            if (ReferenceEquals(e.OriginalSource, host)) return;
            ScheduleScan(st);
        }

        private static void OnDescendantUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement host) return;
            var st = GetState(host);
            if (st == null) return;
            if (ReferenceEquals(e.OriginalSource, host)) return;
            ScheduleScan(st);
        }

        private static void OnAnyChecked(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement host) return;
            var st = GetState(host);
            if (st == null) return;

            var item = FindOwner(e.OriginalSource as DependencyObject, host);
            if (item == null) return;               // 不是本组的项（例如宿主里混了别的 ToggleButton）
            if (!st.Items.Contains(item)) ScheduleScan(st);
            // 把「刚刚被选中的那一项」直接带进去：即使外部数据源没有把同组其它项复位
            //（ItemsControl 里的 RadioButton 逻辑父级是各自的 ContentPresenter，
            //  WPF 的自动互斥不生效），胶囊也一定跟着用户点的那一项走。
            UpdateCore(st, animate: true, reason: "check", preferred: item);
        }

        private static RadioButton? FindOwner(DependencyObject? node, FrameworkElement host)
        {
            int guard = 0;
            while (node != null && guard++ < 64)
            {
                if (node is RadioButton rb) return rb;
                if (ReferenceEquals(node, host)) return null;
                try { node = VisualTreeHelper.GetParent(node); } catch { return null; }
            }
            return null;
        }

        // ── 频率熔断（防布局死循环）────────────────────────────────────
        // ⚠⚠ 为什么需要：下面两个 SizeChanged 处理器都会调 UpdateCore 去重算 + 移动指示块。
        //   如果"指示块尺寸变化"又反过来影响了宿主/分段的尺寸，
        //   就会形成 host/item SizeChanged → UpdateCore → 尺寸再变 → SizeChanged → …
        //   的死循环。宿主/分段在动画或不同 DPI 下尺寸抖动时特别容易触发，
        //   表现就是切换「简洁 ⇄ 网格」时界面直接卡死（用户实测）。
        //
        //   这里加个频率熔断：1 秒内超过 N 次就停手。
        //   **宁可指示块位置差一点，也绝不能卡死。**
        private static int _resizeStreak;
        private static DateTime _resizeStreakStart = DateTime.MinValue;
        private const int ResizeStreakLimit = 60;

        private static bool ResizeAllowed()
        {
            var now = DateTime.UtcNow;
            if ((now - _resizeStreakStart).TotalSeconds > 1.0)
            {
                _resizeStreakStart = now;
                _resizeStreak = 0;
            }
            return ++_resizeStreak <= ResizeStreakLimit;
        }

        private static void OnHostSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is not FrameworkElement host) return;
            var st = GetState(host);
            if (st == null) return;
            if (st.Animating) return;              // 位移在播：绝不把它拽回去
            if (!ResizeAllowed()) return;          // 熔断
            UpdateCore(st, animate: false, reason: "resize");
        }

        private static void OnHostVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (sender is not FrameworkElement host) return;
            var st = GetState(host);
            if (st == null) return;
            if (!host.IsVisible) { st.Positioned = false; st.Shown = false; return; }
            UpdateCore(st, animate: false, reason: "visible");
        }

        private static void OnItemSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (sender is not FrameworkElement item) return;
            var st = (SegmentState?)item.GetValue(OwnerProperty);
            if (st == null) return;
            if (st.Animating) return;
            if (!ResizeAllowed()) return;          // 熔断（见上面 ResizeAllowed 的说明）
            UpdateCore(st, animate: false, reason: "item-resize");
        }

        #endregion

        #region 核心：定位 + 滑动

        private static RadioButton? FindChecked(SegmentState st)
        {
            RadioButton? fallback = null;
            foreach (var item in st.Items)
            {
                if (item.IsChecked != true) continue;
                if (!item.IsVisible) continue;
                if (item.ActualWidth >= 1 && item.ActualHeight >= 1) return item;
                fallback ??= item;
            }
            return fallback;
        }

        private static void Hide(SegmentState st)
        {
            var ind = st.Indicator;
            if (ind == null) return;
            ind.BeginAnimation(UIElement.OpacityProperty, null);
            ind.Opacity = 0;
            st.Shown = false;
            st.Positioned = false;
        }

        /// <summary>
        /// 指示块唯一的定位入口。
        ///   * 位置 = 「新格子」（绝对定位基线）+ 「一个 TranslateTransform」；
        ///   * 起点 = 当前动画值 + 上一次目标 − 新目标 → 连点 / 反向点都不跳；
        ///   * 只动 RenderTransform 与指示块自身的 Width/Height，<b>绝不碰宿主的 Width/Height</b>；
        ///   * 结束走 <see cref="PageTransition.SettleElement"/>：清时钟、位移归零、摘掉动画期 BitmapCache。
        /// </summary>
        private static void UpdateCore(SegmentState st, bool animate, string reason, RadioButton? preferred = null)
        {
            var sw = Stopwatch.StartNew();
            var ind = st.Indicator;
            var host = st.Host;
            if (ind == null) return;

            try
            {
                var target = preferred != null && preferred.IsChecked == true &&
                             preferred.ActualWidth >= 1 && preferred.ActualHeight >= 1
                    ? preferred
                    : FindChecked(st);
                if (target == null) { Hide(st); return; }

                double w = target.ActualWidth, h = target.ActualHeight;
                if (w < 1 || h < 1) return;          // 还没排好版：等 SizeChanged

                // 坐标基准必须是「指示块与分段项的共同祖先」：指示块一般挂在 Canvas 里，
                // 而 Canvas 是分段项的**兄弟**，不是祖先 —— 对 Canvas 做 TransformToAncestor
                // 会抛「指定的 Visual 不是此 Visual 的上级」。所以统一先换算到宿主坐标系，
                // 再按指示块的定位容器（Canvas 或宿主本身）的偏移折算成它自己的局部坐标。
                var origin = target.TransformToAncestor(host).Transform(new Point(0, 0));
                if (VisualTreeHelper.GetParent(ind) is Canvas canvas && !ReferenceEquals(canvas, host))
                {
                    var canvasOrigin = canvas.TransformToAncestor(host).Transform(new Point(0, 0));
                    origin = new Point(origin.X - canvasOrigin.X, origin.Y - canvasOrigin.Y);
                }

                var tr = st.Translate ??= EnsureTranslate(ind);

                // ① 先读出「动画中的当前值」，再清时钟 —— 清完读到的才是基值
                //    Translate 的基值恒为 0，所以可视位置 = 上一次基线 + 当前动画值；
                //    Width/Height 的基值就是上一次的目标，所以当前动画值本身就是可视宽度。
                double curX = tr.X, curY = tr.Y;
                double curW = ind.Width, curH = ind.Height;
                if (double.IsNaN(curW) || curW < 1) curW = w;
                if (double.IsNaN(curH) || curH < 1) curH = h;

                tr.BeginAnimation(TranslateTransform.XProperty, null);
                tr.BeginAnimation(TranslateTransform.YProperty, null);
                ind.BeginAnimation(FrameworkElement.WidthProperty, null);
                ind.BeginAnimation(FrameworkElement.HeightProperty, null);

                bool first = !st.Positioned;
                double prevX = first ? origin.X : st.TargetX;
                double prevY = first ? origin.Y : st.TargetY;
                double prevW = curW, prevH = curH;

                // ② 写新基线（位置交给绝对定位；「旧位置」靠下一行的 from 补回来）
                SetBaseline(ind, origin.X, origin.Y);
                ind.Width = w;
                ind.Height = h;
                double visualX = prevX + (double.IsNaN(curX) ? 0.0 : curX);
                double visualY = prevY + (double.IsNaN(curY) ? 0.0 : curY);
                double fromX = visualX - origin.X;
                double fromY = visualY - origin.Y;

                bool axisX = st.Axis != SegmentAxis.Vertical;
                bool axisY = st.Axis != SegmentAxis.Horizontal;
                if (!axisX) { tr.X = 0; fromX = 0; }
                if (!axisY) { tr.Y = 0; fromY = 0; }

                st.TargetX = origin.X;
                st.TargetY = origin.Y;

                bool moved = Math.Abs(fromX) > 0.5 || Math.Abs(fromY) > 0.5 ||
                             Math.Abs(prevW - w) > 0.5 || Math.Abs(prevH - h) > 0.5;

                bool wasPositioned = st.Positioned;
                st.Positioned = true;

                // ③ 被动刷新且目标没变：什么都不做（尤其别打断正在跑的动画）
                if (!animate && wasPositioned && !moved) return;

                if (!animate || !PageTransition.AnimationsEnabled || !moved)
                {
                    tr.X = 0; tr.Y = 0;
                    ind.BeginAnimation(FrameworkElement.WidthProperty, null);
                    ind.BeginAnimation(FrameworkElement.HeightProperty, null);
                    ind.Width = w; ind.Height = h;
                    st.Animating = false;
                    Show(st, fade: !st.Shown);
                    Log(st, target, reason, 0, 0, fromX, fromY, w, h, sw, skipped: true);
                    return;
                }

                st.Animating = true;
                st.Shown = true;
                ind.Opacity = 1;
                ind.BeginAnimation(UIElement.OpacityProperty, null);

                // 宽度动画期间挂 BitmapCache（受 1.2MP 上限，结束即摘）
                MotionAssist.EnableBitmapCache(ind, true);

                var ease = AxolotlMotion.EaseInOut;
                var dur = AxolotlMotion.Ms(st.Ms);

                if (axisX && Math.Abs(fromX) > 0.01)
                    tr.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, dur) { EasingFunction = ease });
                else { tr.X = 0; }
                if (axisY && Math.Abs(fromY) > 0.01)
                    tr.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, dur) { EasingFunction = ease });
                else { tr.Y = 0; }

                var widthAnim = new DoubleAnimation(prevW, w, dur) { EasingFunction = ease };
                widthAnim.Completed += (s, e) => Finish(st);
                ind.BeginAnimation(FrameworkElement.WidthProperty, widthAnim);

                if (Math.Abs(prevH - h) > 0.5)
                    ind.BeginAnimation(FrameworkElement.HeightProperty, new DoubleAnimation(prevH, h, dur) { EasingFunction = ease });

                Log(st, target, reason, fromX, fromY, fromX, fromY, w, h, sw, skipped: false);
            }
            catch (Exception ex)
            {
                try { Utilities.Logger.LogError(ex, "SegmentedIndicator.UpdateCore"); } catch { }
            }
        }

        /// <summary>动画收尾：强制归位（清时钟 + 位移归零 + 摘 BitmapCache），不留 HoldEnd 残留。</summary>
        private static void Finish(SegmentState st)
        {
            var ind = st.Indicator;
            if (ind == null) return;
            try
            {
                ind.BeginAnimation(FrameworkElement.WidthProperty, null);
                ind.BeginAnimation(FrameworkElement.HeightProperty, null);
                PageTransition.SettleElement(ind, resetOpacity: false);   // 位移归零 + 摘缓存
                var tr = st.Translate;
                if (tr != null)
                {
                    tr.BeginAnimation(TranslateTransform.XProperty, null);
                    tr.BeginAnimation(TranslateTransform.YProperty, null);
                    tr.X = 0; tr.Y = 0;
                }
            }
            catch { }
            finally { st.Animating = false; }
        }

        /// <summary>指示块出现：首次淡入用 NavSlider.vue 那条 250ms cubic-bezier(0.5,0,0.2,1) 延迟 50ms。</summary>
        private static void Show(SegmentState st, bool fade)
        {
            var ind = st.Indicator;
            if (ind == null) return;

            if (!fade || !PageTransition.AnimationsEnabled)
            {
                ind.BeginAnimation(UIElement.OpacityProperty, null);
                ind.Opacity = 1;
                st.Shown = true;
                return;
            }

            var anim = new DoubleAnimation(0, 1, AxolotlMotion.Ms(AxolotlMotion.NavSliderFadeMs))
            {
                EasingFunction = AxolotlMotion.NavSliderFadeEase,
                BeginTime = TimeSpan.FromMilliseconds(AxolotlMotion.NavSliderFadeDelayMs),
            };
            anim.Completed += (s, e) =>
            {
                ind.BeginAnimation(UIElement.OpacityProperty, null);
                ind.Opacity = 1;
            };
            ind.BeginAnimation(UIElement.OpacityProperty, anim);
            st.Shown = true;
        }

        /// <summary>把指示块钉到宿主坐标 (x, y)：Canvas 子元素用 Canvas.Left/Top，其余用 Margin。</summary>
        private static void SetBaseline(FrameworkElement ind, double x, double y)
        {
            if (VisualTreeHelper.GetParent(ind) is Canvas)
            {
                Canvas.SetLeft(ind, x);
                Canvas.SetTop(ind, y);
                return;
            }
            ind.Margin = new Thickness(x, y, 0, 0);
        }

        /// <summary>每个分段组的 id：日志里能一眼看出是哪一组在滑。</summary>
        private static string IdOf(SegmentState st)
            => string.IsNullOrEmpty(st.Host.Name) ? st.Host.GetType().Name : st.Host.Name;

        private static void Log(SegmentState st, RadioButton target, string reason,
            double deltaX, double deltaY, double fromX, double fromY,
            double w, double h, Stopwatch sw, bool skipped)
        {
            try
            {
                sw.Stop();
                string axis = st.Axis == SegmentAxis.Vertical ? "Y" : (st.Axis == SegmentAxis.Both ? "XY" : "X");
                double delta = Math.Abs(deltaY) > Math.Abs(deltaX) ? deltaY : deltaX;
                string what = skipped ? "skipped" : "slide";
                MotionPerf.Note("pill-indicator",
                    sw.Elapsed.TotalMilliseconds,
                    $"id={IdOf(st)} {what} reason={reason} axis={axis} " +
                    $"delta={delta:+0.#;-0.#;0}px deltaX={deltaX:+0.#;-0.#;0}px deltaY={deltaY:+0.#;-0.#;0}px " +
                    $"from={fromX:+0.#;-0.#;0},{fromY:+0.#;-0.#;0}->0 " +
                    $"w={w:0.#} h={h:0.#} ms={st.Ms:0} " +
                    $"target=\"{Trim(target.Content)}\"");
            }
            catch { }
        }

        private static string Trim(object? content)
        {
            string s = content?.ToString() ?? string.Empty;
            s = s.Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= 18 ? s : s.Substring(0, 18);
        }

        #endregion
    }
}
