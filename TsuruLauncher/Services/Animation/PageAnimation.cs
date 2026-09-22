using System;
using System.Windows;
using System.Windows.Controls;

namespace TsuruLauncher.Services.Animation
{
    /// <summary>
    /// XAML 侧的动效挂载点（附加属性）。所有数值都取自 <see cref="AxolotlMotion"/>，
    /// 即 Axolotl 源码里的原始时长 / 曲线 / 位移 / 缩放。
    /// </summary>
    public static class PageAnimation
    {
        #region EnableEnterAnimation —— 页面进场（Axolotl .page-slide + .slide）

        public static readonly DependencyProperty EnableEnterAnimationProperty =
            DependencyProperty.RegisterAttached(
                "EnableEnterAnimation",
                typeof(bool),
                typeof(PageAnimation),
                new PropertyMetadata(false, OnEnableEnterAnimationChanged));

        public static bool GetEnableEnterAnimation(DependencyObject obj) => (bool)obj.GetValue(EnableEnterAnimationProperty);
        public static void SetEnableEnterAnimation(DependencyObject obj, bool value) => obj.SetValue(EnableEnterAnimationProperty, value);

        /// <summary>true = 前进（从下方 +30px 上来），false = 后退（从上方 -30px 下来）。</summary>
        public static readonly DependencyProperty EnterFromBelowProperty =
            DependencyProperty.RegisterAttached(
                "EnterFromBelow",
                typeof(bool),
                typeof(PageAnimation),
                new PropertyMetadata(true));

        public static bool GetEnterFromBelow(DependencyObject obj) => (bool)obj.GetValue(EnterFromBelowProperty);
        public static void SetEnterFromBelow(DependencyObject obj, bool value) => obj.SetValue(EnterFromBelowProperty, value);

        private static void OnEnableEnterAnimationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && (bool)e.NewValue)
            {
                element.Loaded -= OnElementLoaded;
                element.Loaded += OnElementLoaded;
            }
        }

        private static void OnElementLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
            {
                PageTransition.PlayPageEnter(element, GetEnterFromBelow(element));
            }
        }

        #endregion

        #region StaggerChildren —— 列表错峰入场（28ms 步长 / 168ms 上限）

        public static readonly DependencyProperty StaggerChildrenProperty =
            DependencyProperty.RegisterAttached(
                "StaggerChildren",
                typeof(bool),
                typeof(PageAnimation),
                new PropertyMetadata(false, OnStaggerChildrenChanged));

        public static bool GetStaggerChildren(DependencyObject obj) => (bool)obj.GetValue(StaggerChildrenProperty);
        public static void SetStaggerChildren(DependencyObject obj, bool value) => obj.SetValue(StaggerChildrenProperty, value);

        /// <summary>错峰步长，默认 28ms（Axolotl Settings.vue:395 的 index * 28）。</summary>
        public static readonly DependencyProperty StaggerStepMsProperty =
            DependencyProperty.RegisterAttached(
                "StaggerStepMs",
                typeof(double),
                typeof(PageAnimation),
                new PropertyMetadata(AxolotlMotion.StaggerStepMs));

        public static double GetStaggerStepMs(DependencyObject obj) => (double)obj.GetValue(StaggerStepMsProperty);
        public static void SetStaggerStepMs(DependencyObject obj, double value) => obj.SetValue(StaggerStepMsProperty, value);

        private static void OnStaggerChildrenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is Panel panel && (bool)e.NewValue)
            {
                panel.Loaded -= OnPanelLoaded;
                panel.Loaded += OnPanelLoaded;
                panel.LayoutUpdated -= OnPanelLayoutUpdated;
                panel.LayoutUpdated += OnPanelLayoutUpdated;
            }
        }

        private static void OnPanelLoaded(object sender, RoutedEventArgs e)
            => PlayStagger(sender as Panel);

        /// <summary>
        /// 每个面板各自记住上一次的子项数量（原先是一个 static 字段，
        /// 多个列表会互相踩，导致刷新时错峰不重播或乱播）。
        /// </summary>
        private static readonly DependencyProperty LastStaggerCountProperty =
            DependencyProperty.RegisterAttached(
                "LastStaggerCount", typeof(int), typeof(PageAnimation), new PropertyMetadata(-1));

        private static void OnPanelLayoutUpdated(object? sender, EventArgs e)
        {
            // 列表是虚拟化 / 异步填充的：子项数量变化时（例如资源列表刷新 / 类别切换）重播一次错峰入场。
            if (sender is not Panel panel) return;
            if (!panel.IsVisible || panel.Children.Count == 0) return;

            int last = (int)panel.GetValue(LastStaggerCountProperty);
            if (panel.Children.Count == last) return;
            panel.SetValue(LastStaggerCountProperty, panel.Children.Count);
            PlayStagger(panel);
        }

        /// <summary>手动重播一次错峰入场（类别切换 / 搜索刷新时由 code-behind 调用）。</summary>
        public static void ReplayStagger(Panel? panel)
        {
            if (panel == null) return;
            panel.SetValue(LastStaggerCountProperty, panel.Children.Count);
            PlayStagger(panel);
        }

        private static void PlayStagger(Panel? panel)
        {
            if (panel == null) return;
            PageTransition.PlayStaggeredIn(panel, GetStaggerStepMs(panel));
        }

        #endregion

        #region FloatIn —— 通用上浮入场（translation-float-in 0.5s ease-out）

        public static readonly DependencyProperty FloatInProperty =
            DependencyProperty.RegisterAttached(
                "FloatIn",
                typeof(bool),
                typeof(PageAnimation),
                new PropertyMetadata(false, OnFloatInChanged));

        public static bool GetFloatIn(DependencyObject obj) => (bool)obj.GetValue(FloatInProperty);
        public static void SetFloatIn(DependencyObject obj, bool value) => obj.SetValue(FloatInProperty, value);

        public static readonly DependencyProperty FloatInDelayMsProperty =
            DependencyProperty.RegisterAttached(
                "FloatInDelayMs",
                typeof(double),
                typeof(PageAnimation),
                new PropertyMetadata(0.0));

        public static double GetFloatInDelayMs(DependencyObject obj) => (double)obj.GetValue(FloatInDelayMsProperty);
        public static void SetFloatInDelayMs(DependencyObject obj, double value) => obj.SetValue(FloatInDelayMsProperty, value);

        private static void OnFloatInChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && (bool)e.NewValue)
            {
                element.Loaded -= OnFloatInLoaded;
                element.Loaded += OnFloatInLoaded;
            }
        }

        private static void OnFloatInLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
                PageTransition.PlayFloatIn(element, GetFloatInDelayMs(element));
        }

        #endregion

        #region NavItemIn —— 导航按钮入场（0.5s cubic-bezier(0.15, 1.4, 0.64, 0.96)）

        public static readonly DependencyProperty NavItemInProperty =
            DependencyProperty.RegisterAttached(
                "NavItemIn",
                typeof(bool),
                typeof(PageAnimation),
                new PropertyMetadata(false, OnNavItemInChanged));

        public static bool GetNavItemIn(DependencyObject obj) => (bool)obj.GetValue(NavItemInProperty);
        public static void SetNavItemIn(DependencyObject obj, bool value) => obj.SetValue(NavItemInProperty, value);

        private static void OnNavItemInChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && (bool)e.NewValue)
            {
                element.Loaded -= OnNavItemInLoaded;
                element.Loaded += OnNavItemInLoaded;
            }
        }

        private static void OnNavItemInLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
                PageTransition.PlayNavItemIn(element);
        }

        #endregion

        #region FloatingBarIn —— 右下悬浮胶囊（Axolotl FloatingActionBar.vue:265-283）

        public static readonly DependencyProperty FloatingBarInProperty =
            DependencyProperty.RegisterAttached(
                "FloatingBarIn",
                typeof(bool),
                typeof(PageAnimation),
                new PropertyMetadata(false, OnFloatingBarInChanged));

        public static bool GetFloatingBarIn(DependencyObject obj) => (bool)obj.GetValue(FloatingBarInProperty);
        public static void SetFloatingBarIn(DependencyObject obj, bool value) => obj.SetValue(FloatingBarInProperty, value);

        private static void OnFloatingBarInChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element && (bool)e.NewValue)
            {
                element.Loaded -= OnFloatingBarInLoaded;
                element.Loaded += OnFloatingBarInLoaded;
            }
        }

        private static void OnFloatingBarInLoaded(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement element)
                PageTransition.PlayFloatingBar(element, show: true);
        }

        #endregion

        #region AutoCache —— 给动画容器挂 BitmapCache（防重绘卡顿）

        public static readonly DependencyProperty AutoCacheProperty =
            DependencyProperty.RegisterAttached(
                "AutoCache",
                typeof(bool),
                typeof(PageAnimation),
                new PropertyMetadata(false, OnAutoCacheChanged));

        public static bool GetAutoCache(DependencyObject obj) => (bool)obj.GetValue(AutoCacheProperty);
        public static void SetAutoCache(DependencyObject obj, bool value) => obj.SetValue(AutoCacheProperty, value);

        private static void OnAutoCacheChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement element)
                MotionAssist.EnableBitmapCache(element, (bool)e.NewValue);
        }

        #endregion
    }
}
