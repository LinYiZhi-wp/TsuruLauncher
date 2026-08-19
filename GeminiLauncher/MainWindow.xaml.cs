using System.Windows;
using Wpf.Ui.Controls;
using System.Windows.Controls;
using GeminiLauncher.Views;
using GeminiLauncher.Controls;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using System.Windows.Media;
using GeminiLauncher.Services.Animation;
using System.ComponentModel;

namespace GeminiLauncher
{
    public partial class MainWindow : FluentWindow
    {
        private DispatcherTimer? _preloadDismissTimer;
        private bool _isNavigatingBack;

        public MainWindow()
        {
            InitializeComponent();
            this.KeyDown += Window_KeyDown;
            
            RootFrame.Navigating += RootFrame_Navigating;
            this.Loaded += MainWindow_Loaded;
            this.Activated += MainWindow_Activated;
            
            var vm = this.DataContext as ViewModels.MainViewModel;
            if (vm != null)
            {
                vm.RequestNavigation += (page) => RootFrame.Navigate(page);
                vm.RequestGoBack += () => 
                {
                    if (RootFrame.CanGoBack) RootFrame.GoBack();
                };

                vm.NotificationService.OnShowNotification += (msg) => 
                {
                    Dispatcher.Invoke(() => 
                    {
                        NotificationToast? toastRef = null;
                        toastRef = new NotificationToast(msg, () => 
                        {
                            if (toastRef != null && NotificationContainer.Children.Contains(toastRef))
                                NotificationContainer.Children.Remove(toastRef);
                        });
                        NotificationContainer.Children.Add(toastRef);
                    });
                };

                vm.PreloadProgressChanged += (progress, status) =>
                {
                    Dispatcher.Invoke(() => UpdatePreloadNotification(progress, status));
                };

                vm.PreloadCompleted += () =>
                {
                    Dispatcher.Invoke(() => ShowPreloadComplete());
                };

            }
        }

        private void ShowPreloadNotification()
        {
            PreloadNotification.Visibility = Visibility.Visible;
            PageTransition.Play(PreloadNotification, TransitionType.SlideUp);
        }

        private void UpdatePreloadNotification(int progress, string status)
        {
            if (PreloadNotification.Visibility != Visibility.Visible)
                ShowPreloadNotification();

            PreloadNotifProgress.Value = progress;
            PreloadNotifDetail.Text = status;
        }

        private void ShowPreloadComplete()
        {
            PreloadNotifTitle.Text = "资源加载完成";
            PreloadNotifDetail.Text = "";
            PreloadNotifIconText.Text = "✓";
            PreloadNotifIcon.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676"));
            PreloadNotifProgress.Visibility = Visibility.Collapsed;

            _preloadDismissTimer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(3) };
            _preloadDismissTimer.Tick += (s, e) =>
            {
                _preloadDismissTimer.Stop();
                DismissPreloadNotification();
            };
            _preloadDismissTimer.Start();
        }

        private void DismissPreloadNotification()
        {
            PageTransition.Play(PreloadNotification, TransitionType.SlideDown, () =>
            {
                PreloadNotification.Visibility = Visibility.Collapsed;
            });
        }

        private void RootFrame_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
        {
            if (e.Content is Page page)
            {
                page.Width = double.NaN;
                page.Height = double.NaN;
                page.HorizontalAlignment = HorizontalAlignment.Stretch;
                page.VerticalAlignment = VerticalAlignment.Stretch;

                PageTransition.PlayPageEnter(page, !_isNavigatingBack);
                PageTransition.PlayContainerEnter(FrameContainer, !_isNavigatingBack);
                _isNavigatingBack = false;
            }
        }

        private void RootNavigation_BackRequested(NavigationView sender, object args)
        {
            if (RootFrame.CanGoBack)
            {
                _isNavigatingBack = true;
                RootFrame.GoBack();
            }
            else
            {
                if (!(RootFrame.Content is Views.HomePage))
                {
                    NavigateTo("home");
                    // "home" fallback should be a fresh start, not a pushed page —
                    // otherwise repeated back presses keep pushing more HomePages.
                    while (RootFrame.CanGoBack) RootFrame.RemoveBackEntry();
                }
            }
        }


        private void RootNavigation_SelectionChanged(NavigationView sender, RoutedEventArgs args)
        {
            HandleNavigation(sender);
        }

        private void RootNavigation_ItemInvoked(NavigationView sender, RoutedEventArgs args)
        {
            HandleNavigation(sender);
        }

        private void HandleNavigation(NavigationView sender)
        {
            try
            {
                if (sender.SelectedItem is NavigationViewItem item)
                {
                    var tag = item.Tag?.ToString()?.ToLower()?.Trim();
                    NavigateTo(tag);
                }
            }
            catch (System.Exception ex)
            {
                GeminiLauncher.Utilities.Logger.LogError(ex, "HandleNavigation");
                iOS26Dialog.Show($"导航错误: {ex.Message}", "导航错误", DialogIcon.Error);
            }
        }

        public void NavigateTo(string? tag)
        {
            switch (tag)
            {
                case "home":
                    RootFrame.Navigate(new Views.HomePage());
                    break;
                case "resources":
                    RootFrame.Navigate(new Views.ResourcesPage());
                    break;
                case "download":
                    RootFrame.Navigate(new Views.DownloadPage());
                    break;
                case "settings":
                    RootFrame.Navigate(new Views.SettingsPage());
                    break;
            }
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
             RootFrame.Navigate(new Views.HomePage());
             RefreshNavigationVisibility();

             if (DataContext is ViewModels.MainViewModel vm && vm.BackgroundImage == null)
             {
                 try { await vm.LoadBackgroundAsync(); } catch { }
             }
        }

        private DateTime _lastActivatedRefresh = DateTime.MinValue;

        private async void MainWindow_Activated(object? sender, EventArgs e)
        {
            if (DateTime.Now - _lastActivatedRefresh < System.TimeSpan.FromSeconds(5)) return;
            _lastActivatedRefresh = DateTime.Now;

            if (DataContext is ViewModels.MainViewModel vm && !vm.IsLaunching)
            {
                await vm.LoadVersionsAsync();
                if (RootFrame.Content is Views.HomePage homePage)
                {
                    homePage.RefreshOverviewData();
                }
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F12)
            {
                NavigateTo("settings");
            }
        }

        public void RefreshNavigationVisibility()
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                var hiddenKeys = vm.ConfigService.Settings.HiddenPageKeys;

                // Check MenuItems
                foreach (var item in RootNavigation.MenuItems)
                {
                    if (item is NavigationViewItem navItem && navItem.Tag is string tag)
                    {
                        navItem.Visibility = hiddenKeys.Contains(tag.ToLower()) ? Visibility.Collapsed : Visibility.Visible;
                    }
                }

                // Check FooterMenuItems
                foreach (var item in RootNavigation.FooterMenuItems)
                {
                    if (item is NavigationViewItem navItem && navItem.Tag is string tag)
                    {
                        navItem.Visibility = hiddenKeys.Contains(tag.ToLower()) ? Visibility.Collapsed : Visibility.Visible;
                    }
                }
            }
        }

        private void NavItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is NavigationViewItem item)
            {
                var tag = item.Tag?.ToString()?.ToLower()?.Trim();
                NavigateTo(tag);
                e.Handled = true;
            }
        }
        
        private void OpenDownloadPage_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo("download");
        }
    }
}
