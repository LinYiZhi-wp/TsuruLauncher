using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using TsuruLauncher.Services.Animation;
using TsuruLauncher.ViewModels;

namespace TsuruLauncher.Views
{
    public partial class LoaderSelectionPage : Page
    {
        public LoaderSelectionViewModel VM => (LoaderSelectionViewModel)DataContext;

        public LoaderSelectionPage(Models.DownloadableVersion version)
        {
            InitializeComponent();
            DataContext = new LoaderSelectionViewModel();
            VM.Initialize(version);
            VM.PropertyChanged += VM_PropertyChanged;
            Loaded += LoaderSelectionPage_Loaded;
        }

        private void LoaderSelectionPage_Loaded(object sender, RoutedEventArgs e)
        {
            if (LoaderPanel != null)
            {
                PageTransition.PlayStaggeredIn(LoaderPanel, staggerMs: 60);
            }
        }

        private void VM_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // ⚠ 之前只处理「展开」不处理「收起」—— 收起时面板直接消失（硬切）。
            //   现在两个方向都做：淡入淡出 + 位移 + 高度动画。
            switch (e.PropertyName)
            {
                case nameof(LoaderSelectionViewModel.ForgeExpanded):
                    TogglePanel(ForgeExpandPanel, VM.ForgeExpanded);
                    break;
                case nameof(LoaderSelectionViewModel.FabricExpanded):
                    TogglePanel(FabricExpandPanel, VM.FabricExpanded);
                    break;
                case nameof(LoaderSelectionViewModel.OptifineExpanded):
                    TogglePanel(OptifineExpandPanel, VM.OptifineExpanded);
                    break;
            }
        }

        /// <summary>展开 / 收起一个面板（两个方向都有动画）。</summary>
        private static void TogglePanel(FrameworkElement? panel, bool expand)
        {
            if (panel == null) return;

            PageTransition.PlayExpandCollapse(panel, expand);   // 淡入淡出 + 位移
            AnimateExpandHeight(panel, expand);                 // 高度 0 ⇄ 内容实测高度
        }

        /// <summary>
        /// 把 MaxHeight 从 0 动到**内容实测高度**。
        ///
        /// ⚠ 两个坑：
        ///   1) 不能用 Visibility 绑定做展开收起 —— 那是硬切；
        ///   2) 也不能把 MaxHeight 动到一个固定大值（比如 3000）——
        ///      内容只有 200px 时，动画会在 7% 处就"跳"到最终高度，看起来还是硬切。
        ///      必须先用 Measure 量出真实高度。
        /// </summary>
        private static void AnimateExpandHeight(FrameworkElement panel, bool expand)
        {
            try
            {
                double target = 0;
                if (expand)
                {
                    // 量之前要把 MaxHeight 放开，否则 DesiredSize 会被夹到 0
                    double saved = panel.MaxHeight;
                    panel.MaxHeight = double.PositiveInfinity;
                    panel.Measure(new Size(
                        panel.ActualWidth > 0 ? panel.ActualWidth : double.PositiveInfinity,
                        double.PositiveInfinity));
                    target = panel.DesiredSize.Height;
                    panel.MaxHeight = saved;
                }

                panel.BeginAnimation(FrameworkElement.MaxHeightProperty,
                    new DoubleAnimation(panel.MaxHeight, target,
                        TimeSpan.FromMilliseconds(expand ? 280 : 220))
                    {
                        EasingFunction = AxolotlMotion.EaseInOut,
                    });
            }
            catch { }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack)
                NavigationService.GoBack();
            else if (Application.Current.MainWindow is MainWindow mw)
                mw.RootFrame.Navigate(new DownloadPage());
        }

        private void MinimizeDownload_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mw)
                mw.RootFrame.Navigate(new DownloadManagerPage());
        }
    }
}