using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using GeminiLauncher.ViewModels;
using System.Diagnostics;
using System.IO;
using GeminiLauncher.Models;
using GeminiLauncher.Utilities;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Input;
using System.Windows.Shapes;
using System.Threading.Tasks;
using GeminiLauncher.Controls;
using Path = System.IO.Path;

namespace GeminiLauncher.Views
{
    public partial class HomePage : Page
    {
        public HomePage()
        {
            InitializeComponent();
            this.DataContext = ((App)System.Windows.Application.Current).MainWindow.DataContext;

            AutoLoginOffline();
            LoadOverviewData();
        }

        private void AutoLoginOffline()
        {
            if (DataContext is MainViewModel vm)
            {
                // Do NOT silently create an account — the launcher asks the user to
                // log in when they press launch. This keeps logout actually working.
                var activeAccount = vm.AccountManager.ActiveAccount;
                PlayerNameText.Text = activeAccount?.Username ?? "未登录";
                PlayerModeText.Text = activeAccount?.Type == Models.AccountType.Microsoft ? "微软账号" : "离线模式";
                GreetingText.Text = $"👋 你好，{activeAccount?.Username ?? "未登录玩家"}";
                LaunchButton.IsEnabled = true;
            }
        }

        private void LoadOverviewData()
        {
            if (DataContext is MainViewModel vm)
            {
                if (vm.GameVersions.Count == 0)
                {
                    vm.LoadVersionsAsync().ContinueWith(_ =>
                    {
                        Dispatcher.Invoke(() => UpdateOverviewUI(vm));
                    });
                }
                else
                {
                    UpdateOverviewUI(vm);
                }
            }
        }

        public void RefreshOverviewData()
        {
            if (DataContext is MainViewModel vm)
            {
                UpdateOverviewUI(vm);
            }
        }

        private void UpdateOverviewUI(MainViewModel vm)
        {
            VersionCountText.Text = vm.GameVersions.Count.ToString();
            VersionListBox.ItemsSource = vm.GameVersions;

            if (vm.SelectedVersion != null)
            {
                string versionJsonPath = System.IO.Path.Combine(vm.SelectedVersion.RootPath, "versions", vm.SelectedVersion.Id, $"{vm.SelectedVersion.Id}.json");
                if (!System.IO.File.Exists(versionJsonPath))
                {
                    vm.SelectedVersion = vm.GameVersions.FirstOrDefault();
                }
            }

            if (vm.SelectedVersion != null)
            {
                VersionHeroText.Text = vm.SelectedVersion.Id;
                CurrentVersionSubtitle.Text = $"{vm.SelectedVersion.Type} ({vm.SelectedVersion.Id})";
                _currentSelectedVersion = vm.SelectedVersion;

                string modsPath = Path.Combine(vm.SelectedVersion.GameDir, "mods");
                if (Directory.Exists(modsPath))
                {
                    try
                    {
                        int modCount = Directory.GetFiles(modsPath, "*.jar").Length;
                        ModCountText.Text = modCount.ToString();
                    }
                    catch { }
                }
                else
                {
                    ModCountText.Text = "0";
                }
            }
            else
            {
                VersionHeroText.Text = "未选择版本";
                CurrentVersionSubtitle.Text = "请在版本列表中选择";
                ModCountText.Text = "0";
            }

            // Java registry scanning can take a moment — keep it off the UI thread
            _ = Task.Run(() =>
            {
                var installations = new Services.JavaService().FindInstallations();
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (installations.Count > 0)
                    {
                        var best = installations[0];
                        JavaStatusText.Text = best.Version ?? "已安装";
                        JavaStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#00E676")!);
                    }
                    else
                    {
                        JavaStatusText.Text = "未检测";
                        JavaStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                            (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FFC107")!);
                    }
                });
            });
        }

        private void AccountCapsule_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var capsule = sender as FrameworkElement;
            if (capsule == null) return;

            var darkBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E01C1C24")!);
            var hoverItem = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#35FFFFFF")!);
            var accentGreen = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676")!);
            var white = Brushes.White;

            var popup = new Popup
            {
                AllowsTransparency = true,
                StaysOpen = true,
                PlacementTarget = capsule,
                Placement = PlacementMode.Bottom,
                VerticalOffset = 4,
                HorizontalOffset = -20,
                PopupAnimation = PopupAnimation.Fade
            };

            var panel = new StackPanel
            {
                Width = 220,
                SnapsToDevicePixels = true
            };

            var panelBorder = new Border
            {
                Background = darkBg,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(6),
                Child = panel
            };
            panelBorder.Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 5, Opacity = 0.35, Color = Colors.Black };

            Action<string, System.Windows.Input.MouseButtonEventHandler> makeItem = (text, handler) =>
            {
                var itemBorder = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 11, 14, 11),
                    Margin = new Thickness(3, 2, 3, 2),
                    Cursor = Cursors.Hand,
                    Background = Brushes.Transparent
                };
                var itemTb = new TextBlock { Text = text, FontSize = 14, Foreground = white };
                itemBorder.Child = itemTb;
                itemBorder.MouseEnter += (_, __) => itemBorder.Background = hoverItem;
                itemBorder.MouseLeave += (_, __) => itemBorder.Background = Brushes.Transparent;
                itemBorder.PreviewMouseLeftButtonDown += (s, args) =>
                {
                    args.Handled = true;
                    handler(s, args);
                    ClosePopup();
                };
                panel.Children.Add(itemBorder);
            };

            makeItem("🔄 切换离线账号", (s, args) =>
            {
                var dlg = new Window
                {
                    Title = "切换离线账号",
                    Width = 340, Height = 160,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    Background = darkBg,
                    ResizeMode = ResizeMode.NoResize,
                    WindowStyle = WindowStyle.SingleBorderWindow
                };
                var sp = new StackPanel { Margin = new Thickness(24) };
                sp.Children.Add(new TextBlock { Text = "输入玩家名:", Foreground = white, FontSize = 14, Margin = new Thickness(0, 0, 0, 8) });
                var tb = new TextBox
                {
                    Text = "Player", FontSize = 14, Padding = new Thickness(12, 10, 12, 10),
                    Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30202028")!),
                    Foreground = white,
                    BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#30FFFFFF")!),
                    CaretBrush = accentGreen
                };
                sp.Children.Add(tb);
                var okBtn = new Button
                {
                    Content = "确定", Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(28, 8, 28, 8),
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Foreground = white, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand,
                    Background = accentGreen, BorderThickness = new Thickness(0)
                };
                okBtn.Click += (_, __) => { dlg.DialogResult = true; };
                sp.Children.Add(okBtn);
                dlg.Content = sp;
                if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(tb.Text))
                {
                    var vm = DataContext as MainViewModel;
                    vm?.AccountManager.LoginOffline(tb.Text.Trim());
                    PlayerNameText.Text = tb.Text.Trim();
                    GreetingText.Text = $"👋 你好，{tb.Text.Trim()}";
                }
            });

            makeItem("🟢 微软账号登录", (s, args) =>
            {
                _ = LoginMicrosoftAsync();
            });

            var sepLine = new Rectangle
            {
                Height = 1,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#25FFFFFF")!),
                Margin = new Thickness(12, 6, 12, 6)
            };
            panel.Children.Add(sepLine);

            makeItem("🚪 注销", (s, args) =>
            {
                var vm = DataContext as MainViewModel;
                vm?.AccountManager.Logout();
                PlayerNameText.Text = "Player";
                GreetingText.Text = "👋 你好，Player";
            });

            System.Windows.Input.MouseButtonEventHandler? windowHandler = null;
            void ClosePopup()
            {
                popup.IsOpen = false;
                windowHandler = null;
            }

            windowHandler = (s2, e2) =>
            {
                if (!popup.IsOpen) return;
                var hitResult = VisualTreeHelper.HitTest(panelBorder, e2.GetPosition(panelBorder));
                if (hitResult == null)
                {
                    ClosePopup();
                }
            };

            var mainWindow = Window.GetWindow(this);
            if (mainWindow != null)
                mainWindow.PreviewMouseLeftButtonDown += windowHandler;

            popup.Closed += (_, __) =>
            {
                if (mainWindow != null && windowHandler != null)
                    mainWindow.PreviewMouseLeftButtonDown -= windowHandler;
            };

            popup.Child = panelBorder;
            popup.IsOpen = true;
        }

        private async Task LoginMicrosoftAsync()
        {
            try
            {
                var vm = DataContext as MainViewModel;
                if (vm != null)
                {
                    await vm.AccountManager.LoginMicrosoft();
                    var acc = vm.AccountManager.ActiveAccount;
                    if (acc != null)
                    {
                        PlayerNameText.Text = acc.Username;
                        GreetingText.Text = $"👋 你好，{acc.Username}";
                    }
                }
            }
            catch (System.Exception ex)
            {
                iOS26Dialog.Show($"登录失败: {ex.Message}", "错误", DialogIcon.Error);
            }
        }

        private Models.GameInstance? _currentSelectedVersion;

        private void OpenVersionSelector_Click(object sender, RoutedEventArgs e)
        {
            NavigationService.Navigate(new VersionSelectorPage(async (selectedGv) =>
            {
                if (DataContext is MainViewModel vm)
                {
                    var match = vm.GameVersions.FirstOrDefault(v => v.Id == selectedGv.Id);
                    if (match != null)
                    {
                        vm.SelectedVersion = match;
                        _currentSelectedVersion = match;
                        VersionHeroText.Text = match.Id;
                        CurrentVersionSubtitle.Text = $"{match.Type} ({match.Id})";
                    }
                    else
                    {
                        await vm.LoadVersionsAsync();
                        match = vm.GameVersions.FirstOrDefault(v => v.Id == selectedGv.Id);
                        if (match != null)
                        {
                            vm.SelectedVersion = match;
                            _currentSelectedVersion = match;
                            VersionHeroText.Text = match.Id;
                            CurrentVersionSubtitle.Text = $"{match.Type} ({match.Id})";
                        }
                    }
                    LoadOverviewData();
                }
            }));
        }

        private void OpenVersionSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSelectedVersion == null)
            {
                iOS26Dialog.Show(GetString("Msg_SelectVersionFirst"), GetString("Title_Warning"), DialogIcon.Info);
                return;
            }
            NavigationService.Navigate(new VersionSettingsPage(_currentSelectedVersion));
        }

        private void OpenModManager_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm || vm.SelectedVersion == null)
            {
                iOS26Dialog.Show(GetString("Msg_SelectVersionFirst"), GetString("Title_Warning"), DialogIcon.Info);
                return;
            }

            var btn = sender as FrameworkElement;
            if (btn == null) return;

            var darkBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E01C1C24")!);
            var hoverItem = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#35FFFFFF")!);
            var white = Brushes.White;

            var popup = new Popup
            {
                AllowsTransparency = true,
                StaysOpen = true,
                PlacementTarget = btn,
                Placement = PlacementMode.Bottom,
                VerticalOffset = 4,
                HorizontalOffset = -40,
                PopupAnimation = PopupAnimation.Fade
            };

            var panel = new StackPanel
            {
                Width = 200,
                SnapsToDevicePixels = true
            };

            var panelBorder = new Border
            {
                Background = darkBg,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(6),
                Child = panel
            };
            panelBorder.Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 5, Opacity = 0.35, Color = Colors.Black };

            Action<string, System.Windows.Input.MouseButtonEventHandler> makeItem = (text, handler) =>
            {
                var itemBorder = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(14, 11, 14, 11),
                    Margin = new Thickness(3, 2, 3, 2),
                    Cursor = Cursors.Hand,
                    Background = Brushes.Transparent
                };
                var itemTb = new TextBlock { Text = text, FontSize = 14, Foreground = white };
                itemBorder.Child = itemTb;
                itemBorder.MouseEnter += (_, __) => itemBorder.Background = hoverItem;
                itemBorder.MouseLeave += (_, __) => itemBorder.Background = Brushes.Transparent;
                itemBorder.PreviewMouseLeftButtonDown += (s, args) =>
                {
                    args.Handled = true;
                    handler(s, args);
                    ClosePopup();
                };
                panel.Children.Add(itemBorder);
            };

            makeItem("📂 打开 Mods 文件夹", (s, args) =>
            {
                try
                {
                    string modPath = Path.Combine(vm.SelectedVersion.GameDir, "mods");
                    Directory.CreateDirectory(modPath);
                    Process.Start("explorer.exe", modPath);
                }
                catch (System.Exception ex)
                {
                    iOS26Dialog.Show($"无法打开文件夹: {ex.Message}", "错误", DialogIcon.Error);
                }
            });

            makeItem("⚙️ 管理模组", (s, args) =>
            {
                if (_currentSelectedVersion != null)
                {
                    NavigationService.Navigate(new VersionSettingsPage(_currentSelectedVersion, "mods"));
                }
            });

            System.Windows.Input.MouseButtonEventHandler? windowHandler = null;
            void ClosePopup()
            {
                popup.IsOpen = false;
                windowHandler = null;
            }

            windowHandler = (s2, e2) =>
            {
                if (!popup.IsOpen) return;
                var hitResult = VisualTreeHelper.HitTest(panelBorder, e2.GetPosition(panelBorder));
                if (hitResult == null) ClosePopup();
            };

            var mainWindow = Window.GetWindow(this);
            if (mainWindow != null)
                mainWindow.PreviewMouseLeftButtonDown += windowHandler;

            popup.Closed += (_, __) =>
            {
                if (mainWindow != null && windowHandler != null)
                    mainWindow.PreviewMouseLeftButtonDown -= windowHandler;
            };

            popup.Child = panelBorder;
            popup.IsOpen = true;
        }

        private void OpenGameDir_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                string gamePath = vm.ConfigService.Settings.GamePath;
                if (!string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath))
                {
                    Process.Start("explorer.exe", gamePath);
                }
                else
                {
                    iOS26Dialog.Show(GetString("Msg_GameDirNotFound"), GetString("Title_Warning"), DialogIcon.Warning);
                }
            }
        }

        private void OpenModsDir_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm && vm.SelectedVersion != null)
            {
                string modPath = Path.Combine(vm.SelectedVersion.GameDir, "mods");
                try
                {
                    Directory.CreateDirectory(modPath);
                    Process.Start("explorer.exe", modPath);
                }
                catch (System.Exception ex)
                {
                    iOS26Dialog.Show($"无法打开文件夹: {ex.Message}", "错误", DialogIcon.Error);
                }
            }
            else
            {
                iOS26Dialog.Show(GetString("Msg_SelectVersionFirst"), GetString("Title_Warning"), DialogIcon.Info);
            }
        }

        private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logPath = Logger.GetLogPath();
                if (File.Exists(logPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{logPath}\"");
                }
                else
                {
                    string? dir = Path.GetDirectoryName(logPath);
                    if (dir != null && Directory.Exists(dir))
                    {
                        Process.Start("explorer.exe", dir);
                    }
                    else
                    {
                        iOS26Dialog.Show(GetString("Msg_LogNotGenerated"), GetString("Title_Warning"), DialogIcon.Info);
                    }
                }
            }
            catch (System.Exception ex)
            {
                iOS26Dialog.Show($"无法打开日志: {ex.Message}", "错误", DialogIcon.Error);
            }
        }

        private void OpenLogFolder_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            OpenLogFolder_Click(sender, new RoutedEventArgs());
        }

        private void VersionListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ListBox listBox && listBox.SelectedItem is GameInstance selected)
            {
                if (DataContext is MainViewModel vm)
                {
                    vm.SelectedVersion = selected;
                    _currentSelectedVersion = selected;
                    VersionHeroText.Text = selected.Id;
                    CurrentVersionSubtitle.Text = $"{selected.Type} ({selected.Id})";
                    LoadOverviewData();
                }
            }
        }

        private void CancelLaunch_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.IsLaunching = false;
                vm.StatusMessage = "准备就绪";
            }
        }

        private string GetString(string key)
        {
            try
            {
                if (Application.Current.FindResource(key) is string s) return s;
            }
            catch { }
            return key;
        }
    }
}
