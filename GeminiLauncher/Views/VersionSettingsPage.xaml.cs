using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.IO.Compression;
using GeminiLauncher.Models;
using GeminiLauncher.Services.Animation;
using Newtonsoft.Json.Linq;

namespace GeminiLauncher.Views
{
    public partial class VersionSettingsPage : Page
    {
        private GameInstance _version;
        private VersionSettings? _settings;
        private string _startTab = "overview";
        private string _customJvmArgs = string.Empty;
        private TextBox? _jvmArgsBox;
        private ComboBox? _javaCombo;

        public VersionSettingsPage(GameInstance version)
        {
            InitializeComponent();
            _version = version;
            InitializePage();
        }

        public VersionSettingsPage(GameInstance version, string startTab) : this(version)
        {
            _startTab = startTab;
        }

        private void InitializePage()
        {
            _settings = LoadVersionSettings(_version.Id);
            
            SubtitleText.Text = _version.Id;
            
            if (_startTab == "mods")
            {
                 Dispatcher.BeginInvoke(() => TabButton_Click(TabMods, new RoutedEventArgs()));
            }
            else
            {
                 ShowOverviewTab();
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            SaveVersionSettings();
            if (NavigationService.CanGoBack) NavigationService.GoBack();
        }



        private VersionSettings LoadVersionSettings(string versionId)
        {
            var settings = new VersionSettings
            {
                VersionId = versionId,
                CustomName = _version.Id,
                VersionIsolation = true,
                MemoryMode = MemoryAllocation.Auto,
                MinMemoryMB = 512,
                MaxMemoryMB = 4096
            };

            try
            {
                string configPath = Path.Combine(_version.GameDir, "lyzl_profile.json");
                if (File.Exists(configPath))
                {
                    var json = JObject.Parse(File.ReadAllText(configPath));
                    if (json.ContainsKey("UseGlobalSettings"))
                        settings.MemoryMode = (bool?)json["UseGlobalSettings"] == false
                            ? MemoryAllocation.Custom
                            : MemoryAllocation.FollowGlobal;
                    if (json.ContainsKey("CustomMemoryMb")) settings.MaxMemoryMB = (int?)json["CustomMemoryMb"] ?? 4096;
                    if (json.ContainsKey("CustomMinMemoryMb")) settings.MinMemoryMB = (int?)json["CustomMinMemoryMb"] ?? 512;
                    if (json.ContainsKey("CustomJavaPath")) settings.JavaPath = json["CustomJavaPath"]?.ToString() ?? "";
                    if (json.ContainsKey("CustomJvmArgs")) _customJvmArgs = json["CustomJvmArgs"]?.ToString() ?? "";
                }
            }
            catch { }

            return settings;
        }

        private void SaveVersionSettings()
        {
            try
            {
                var json = new JObject();
                json["UseGlobalSettings"] = _settings?.MemoryMode == MemoryAllocation.Custom ? false : true;
                json["CustomMemoryMb"] = _settings?.MaxMemoryMB ?? 4096;
                json["CustomMinMemoryMb"] = _settings?.MinMemoryMB ?? 512;
                json["CustomJavaPath"] = _settings?.JavaPath ?? "";
                json["CustomJvmArgs"] = _customJvmArgs ?? "";

                string configPath = Path.Combine(_version.GameDir, "lyzl_profile.json");
                File.WriteAllText(configPath, json.ToString());
            }
            catch { }
        }

        private void TabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tag)
            {
                TabOverview.IsEnabled = true;
                TabSettings.IsEnabled = true;
                TabMods.IsEnabled = true;
                TabExport.IsEnabled = true;
                TabMaintenance.IsEnabled = true; // Not used in screenshots but kept for consistency
                
                button.IsEnabled = false;
                
                switch (tag)
                {
                    case "overview": SwitchToTab(() => CreateOverviewContent()); break;
                    case "settings": SwitchToTab(() => ShowSettingsTab()); break;
                    case "mods": SwitchToTab(() => ShowModsTab()); break;
                    case "export": SwitchToTab(() => ShowExportTab()); break;
                    case "maintenance": SwitchToTab(() => ShowMaintenanceTab()); break;
                }
            }
        }

        private void SwitchToTab(Action loadAction)
        {
            ContentArea.Children.Clear();
            loadAction();
            
            foreach (UIElement content in ContentArea.Children)
            {
                // Set initial state for animation
                content.Opacity = 0;
                content.RenderTransform = new System.Windows.Media.TranslateTransform(0, 20);
                
                // Animate
                var storyboard = new System.Windows.Media.Animation.Storyboard();
                
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.28))
                {
                    EasingFunction = TransitionConfig.DecelerateEase
                };
                System.Windows.Media.Animation.Storyboard.SetTarget(fadeIn, content);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));
                
                var slideUp = new System.Windows.Media.Animation.DoubleAnimation(18, 0, TimeSpan.FromSeconds(0.3))
                {
                    EasingFunction = TransitionConfig.SoftSpringEase
                };
                System.Windows.Media.Animation.Storyboard.SetTarget(slideUp, content);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(slideUp, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
                
                storyboard.Children.Add(fadeIn);
                storyboard.Children.Add(slideUp);
                storyboard.Begin();
            }
        }

        // Adjust return types of existing Show...Tab methods to UIElement
        // Currently they return void and modify ContentArea directly.
        // I need to refactor them to return UIElement instead.
        
        // Wait, the current implementation of Show...Tab modifies ContentArea directly.
        // I should stick to that pattern but wrap the modification in animation.
        // Or refactor them. Refactoring is cleaner but riskier with replace_file_content on large file.
        // Let's modify SwitchToTab to accept Action, run it, then animate the children of ContentArea.
        
        private void SwitchToTab2(Action action)
        {
             ContentArea.Children.Clear();
             action();
             
             // Animate all children added
             foreach (UIElement child in ContentArea.Children)
             {
                child.Opacity = 0;
                child.RenderTransform = new System.Windows.Media.TranslateTransform(0, 20);
                
                var storyboard = new System.Windows.Media.Animation.Storyboard();
                var fadeIn = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromSeconds(0.3)) { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
                System.Windows.Media.Animation.Storyboard.SetTarget(fadeIn, child);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(fadeIn, new PropertyPath("Opacity"));
                
                var slideUp = new System.Windows.Media.Animation.DoubleAnimation(20, 0, TimeSpan.FromSeconds(0.3)) { EasingFunction = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut } };
                System.Windows.Media.Animation.Storyboard.SetTarget(slideUp, child);
                System.Windows.Media.Animation.Storyboard.SetTargetProperty(slideUp, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
                
                storyboard.Children.Add(fadeIn);
                storyboard.Children.Add(slideUp);
                storyboard.Begin();
             }
        }
        
        // Let's use the second approach (SwitchToTab2 renaming to SwitchToTab) to minimize changes.


        private void ShowMaintenanceTab()
        {
            ContentArea.Children.Clear();
            var panel = new StackPanel();

            panel.Children.Add(CreateSectionHeader("版本维护"));
            
            var card = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 255, 255)),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 20)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = "⚠️ 危险操作区", Foreground = System.Windows.Media.Brushes.Orange, FontWeight = FontWeights.Bold, Margin = new Thickness(0,0,0,15) });
            
            stack.Children.Add(CreateActionButton("🗑️ 删除该版本", async (s, e) => await DeleteVersionAsync(), "#30FF0000"));
            stack.Children.Add(CreateActionButton("📂 打开版本文件夹", (s, e) => OpenFolder(_version.GameDir)));
            
            card.Child = stack;
            panel.Children.Add(card);
            
            ContentArea.Children.Add(panel);
        }

        // --- Export Tab (Screenshot 1) ---
        private void ShowExportTab()
        {
            ContentArea.Children.Clear();
            var panel = new StackPanel();

            // Modpack Info Input
            var infoCard = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 255, 255)),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 20)
            };
            var infoGrid = new Grid();
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            infoGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            infoGrid.Children.Add(new TextBlock { Text = GetString("Export_ModpackName"), VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0,0,10,0) });
            var nameBox = CreateTextBox(_version.Id);
            Grid.SetColumn(nameBox, 1);
            infoGrid.Children.Add(nameBox);

            var verLabel = new TextBlock { Text = GetString("Export_ModpackVersion"), VerticalAlignment = VerticalAlignment.Center, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(20,0,10,0) };
            Grid.SetColumn(verLabel, 2);
            infoGrid.Children.Add(verLabel);

            var verBox = CreateTextBox("1.0.0");
            Grid.SetColumn(verBox, 3);
            infoGrid.Children.Add(verBox);
            
            infoCard.Child = infoGrid;
            panel.Children.Add(infoCard);

            // Export Content List
            panel.Children.Add(CreateSectionHeader(GetString("Export_Section_Content")));
            var exportList = new StackPanel();

            // Store references to checkboxes to read their state later
            var chkGameCore = CreateCheckbox($"{GetString("Export_GameCore")} {_version.Type} {_version.Id}", true, false); 
            
            var chkSettings = CreateCheckbox(GetString("Export_GameSettings"), true);
            var chkSaves = CreateCheckbox(GetString("Export_Saves"), false);
            // (launcher / launcher-settings options were placeholders with no effect — removed)
            
            exportList.Children.Add(chkGameCore);
            exportList.Children.Add(chkSettings);
            exportList.Children.Add(chkSaves);
            
            var listBorder = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 255, 255)),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(20),
                Child = exportList,
                Margin = new Thickness(0,0,0,20)
            };
            panel.Children.Add(listBorder);

            // Export Button
            var startBtn = new Button
            {
                Content = GetString("Export_Button_Start"),
                Padding = new Thickness(30, 10, 30, 10),
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)),
                Foreground = System.Windows.Media.Brushes.White,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = System.Windows.Input.Cursors.Hand,
                Style = (Style)FindResource(typeof(Button))
            };
            
            // Progress Bar (Initially Collapsed)
            var progressBar = new ProgressBar
            {
                Height = 4,
                Margin = new Thickness(0, 10, 0, 0),
                Visibility = Visibility.Collapsed,
                IsIndeterminate = true,
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 230, 118))
            };

            startBtn.Click += async (s, e) => 
            {
                // 1. Select Export Path
                var saveFileDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "Zip Archive (*.zip)|*.zip",
                    FileName = $"{nameBox.Text}-{verBox.Text}.zip"
                };

                if (saveFileDialog.ShowDialog() == true)
                {
                    startBtn.IsEnabled = false;
                    startBtn.Content = GetString("Export_Exporting");
                    progressBar.Visibility = Visibility.Visible;

                    try
                    {
                        var options = new GeminiLauncher.Models.ExportOptions
                        {
                            ExportPath = saveFileDialog.FileName,
                            ModpackName = nameBox.Text,
                            ModpackVersion = verBox.Text,
                            IncludeGameCore = chkGameCore.IsChecked == true,
                            IncludeGameSettings = chkSettings.IsChecked == true,
                            IncludeSaves = chkSaves.IsChecked == true,
                            IncludeMods = true,
                            IncludeResourcePacks = true,
                            IncludeShaderPacks = true
                        };

                        await GeminiLauncher.Services.GameExportService.ExportGameAsync(_version, options, null); // Pass progress?

                        MessageBox.Show(GetString("Export_Success_Message"), GetString("Export_Success_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"导出失败: {ex.Message}", GetString("Export_Error_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    finally
                    {
                        startBtn.IsEnabled = true;
                        startBtn.Content = GetString("Export_Button_Start");
                        progressBar.Visibility = Visibility.Collapsed;
                    }
                }
            };
            
            var btnContainer = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
            btnContainer.Children.Add(startBtn);
            btnContainer.Children.Add(progressBar);
            panel.Children.Add(btnContainer);

            ContentArea.Children.Add(panel);
        }

        // --- Mod Tab (Screenshot 2: Detection) ---
        private void ShowModsTab()
        {
            ContentArea.Children.Clear();
            var panel = new StackPanel();

            string modsPath = GetModsPath();
            bool isModLoader = InspectForModLoader();

            if (!isModLoader)
            {
                // Show "Mods Not Available" UI (Screenshot 2)
                var border = new Border
                {
                    Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 255, 255)), // Dark theme semi-transparent
                    CornerRadius = new CornerRadius(24),
                    Padding = new Thickness(40),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 50, 0, 0),
                    Width = 600
                };
                
                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                stack.Children.Add(new TextBlock 
                { 
                    Text = "该版本不可使用 Mod", 
                    FontSize = 24, 
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)), // Blue title
                    HorizontalAlignment = HorizontalAlignment.Center, 
                    Margin = new Thickness(0, 0, 0, 20) 
                });
                
                stack.Children.Add(new TextBlock 
                { 
                    Text = "你需要先安装 Forge、Fabric 等 Mod 加载器才能使用 Mod，请在下载页面安装这些版本。\n如果你已经安装过了 Mod 加载器，那你很可能选择了错误的版本，请点击版本选择按钮切换版本。", 
                    FontSize = 14, 
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 255, 255, 255)), // Light Gray for dark theme
                    TextWrapping = TextWrapping.Wrap, 
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 30)
                });
                
                var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
                
                var downloadBtn = new Button 
                { 
                    Content = "转到下载页面", 
                    Padding = new Thickness(20, 10, 20, 10), 
                    Margin = new Thickness(0, 0, 20, 0),
                    Background = System.Windows.Media.Brushes.Transparent, 
                    Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)),
                    BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)),
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                // Fallback style if template is missing, manual styling
                downloadBtn.Style = null;  

                downloadBtn.Click += (s, e) => 
                {
                    if (Application.Current.MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.NavigateTo("download");
                    }
                };

                var selectBtn = new Button 
                { 
                    Content = "版本选择", 
                    Padding = new Thickness(20, 10, 20, 10),
                    Background = System.Windows.Media.Brushes.Transparent,
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderBrush = System.Windows.Media.Brushes.Gray,
                    BorderThickness = new Thickness(1),
                    Cursor = System.Windows.Input.Cursors.Hand
                };
                selectBtn.Style = null; // No template
                selectBtn.Click += (s, e) => 
                { 
                     // Navigate to VersionSelectorPage
                     // Pass a callback that will be executed when a version is selected.
                     // Use Dispatcher to ensure navigation happens AFTER VersionSelectorPage pops itself from the stack.
                     NavigationService.Navigate(new VersionSelectorPage(v => 
                     {
                         Dispatcher.BeginInvoke(new Action(() => 
                         {
                             var newInstance = new GameInstance 
                             {
                                 Id = v.Id,
                                 RootPath = v.GamePath,
                                 GameDir = v.GamePath,
                                 Type = v.Type.ToString().ToLower()
                             };
                             NavigationService.Navigate(new VersionSettingsPage(newInstance));
                         }), System.Windows.Threading.DispatcherPriority.ContextIdle);
                     }));
                };

                btnPanel.Children.Add(downloadBtn);
                btnPanel.Children.Add(selectBtn);
                stack.Children.Add(btnPanel);
                
                border.Child = stack;
                panel.Children.Add(border);
            }
            else
            {
                // Show Mod List
                
                // Header
                var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 15) };
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var titleStack = new StackPanel();
                titleStack.Children.Add(new TextBlock
                {
                    Text = "Mod 管理",
                    FontSize = 20,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = System.Windows.Media.Brushes.White
                });
                titleStack.Children.Add(new TextBlock
                {
                    Text = modsPath,
                    FontSize = 11,
                    Opacity = 0.5,
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                headerGrid.Children.Add(titleStack);

                 var actionPanel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(actionPanel, 1);
                actionPanel.Children.Add(CreateActionButton("🔄 刷新", (s, e) => ShowModsTab()));
                actionPanel.Children.Add(CreateActionButton("📁 打开文件夹", (s, e) => OpenFolder(modsPath)));
                headerGrid.Children.Add(actionPanel);
                panel.Children.Add(headerGrid);

                 if (!System.IO.Directory.Exists(modsPath))
                {
                    panel.Children.Add(new TextBlock { Text = "暂无 Mods 文件夹", Foreground = System.Windows.Media.Brushes.White });
                }
                else
                {
                     var modFiles = System.IO.Directory.GetFiles(modsPath)
                        .Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || 
                                    f.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(f => System.IO.Path.GetFileName(f))
                        .ToArray();
                     
                      foreach (var modPath in modFiles)
                    {
                        panel.Children.Add(CreateModItem(modPath));
                    }
                }
            }
            
            ContentArea.Children.Add(panel);
        }

        private bool InspectForModLoader()
        {
            // Heuristic check
            if (_version.Id.Contains("Fabric", StringComparison.OrdinalIgnoreCase) || 
                _version.Id.Contains("Forge", StringComparison.OrdinalIgnoreCase) ||
                _version.InheritsFrom.Any(i => i.Contains("forge") || i.Contains("fabric")))
            {
                return true;
            }
            // Check libraries (if loaded)
            if (_version.Libraries.Any(l => l.Name.Contains("minecraftforge") || l.Name.Contains("fabricmc")))
            {
                return true;
            }
            return false;
        }

        // --- Settings Tab (Screenshots 3 & 4) ---
        private void ShowSettingsTab()
        {
            ContentArea.Children.Clear();
            var panel = new StackPanel();

            // Alert
            var alertBorder = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 33, 150, 243)),
                BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(100, 33, 150, 243)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(15, 10, 15, 10),
                Margin = new Thickness(0, 0, 0, 20)
            };
            alertBorder.Child = new TextBlock
            {
                Text = "ℹ️ 这些设置只对该游戏版本生效，不影响其他版本。",
                Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(100, 181, 246)),
                FontSize = 13
            };
            panel.Children.Add(alertBorder);

            // Group 1: Launch Options
            var launchItems = new List<UIElement>();
            launchItems.Add(CreateSettingItem("版本隔离", CreateComboBox(new[] { "开启", "关闭" })));
            launchItems.Add(CreateSettingItem("游戏窗口标题", CreateTextBox("跟随全局设置")));
            launchItems.Add(CreateSettingItem("自定义信息", CreateTextBox("跟随全局设置"))); // From screenshot 3
            
            // Java with Auto/Global logic (persisted to lyzl_profile.json)
            _javaCombo = CreateComboBox(new[] { "跟随全局设置", "智能匹配 (Auto)", "自定义..." }, 0);
            _javaCombo.SelectionChanged += JavaCombo_SelectionChanged;
            launchItems.Add(CreateSettingItem("游戏 Java", _javaCombo));
            
            panel.Children.Add(CreateSettingsGroup("启动选项", launchItems.ToArray()));

            // Group 2: Game Memory (Screenshot 3)
            var memPanel = new StackPanel();
            
            // Radio Buttons
            var radioPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0,0,0,15) };
            radioPanel.Children.Add(CreateRadioButton("跟随全局设置", true));
            radioPanel.Children.Add(CreateRadioButton("自动配置", false));
            radioPanel.Children.Add(CreateRadioButton("自定义", false));
            memPanel.Children.Add(CreateSettingItem("游戏内存", radioPanel));

            // Slider
             memPanel.Children.Add(CreateSettingItem(" ", CreateRealMemorySlider())); // Indented slider

            // Optimization Dropdown
            memPanel.Children.Add(CreateSettingItem("启动游戏前进行内存优化", CreateComboBox(new[] { "跟随全局设置", "开启", "关闭" })));
            
            // Usage Bar (Visual Mockup)
            var usageGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            usageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            usageGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            
            var barGrid = new Grid { Height = 4, Margin = new Thickness(0,0,0,5) };
            barGrid.Children.Add(new Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 255, 255, 255)), CornerRadius = new CornerRadius(2) });
            barGrid.Children.Add(new Border { Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(33, 150, 243)), Width = 250, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(2) }); // Blue part
            
            var textGrid = new Grid();
            textGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            textGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            long totalMemMb = GeminiLauncher.Services.LaunchService.GetTotalSystemMemoryMB();
            int allocMb = _settings?.MaxMemoryMB ?? 4096;
            textGrid.Children.Add(new TextBlock { Text = $"系统内存 {totalMemMb / 1024.0:F1} GB", Foreground = System.Windows.Media.Brushes.Gray, FontSize = 12 });
            var allocText = new TextBlock { Text = $"游戏分配 {allocMb / 1024.0:F1} GB", Foreground = System.Windows.Media.Brushes.Gray, FontSize = 12 };
            Grid.SetColumn(allocText, 1);
            textGrid.Children.Add(allocText);

            usageGrid.Children.Add(barGrid);
            Grid.SetRow(textGrid, 1);
            usageGrid.Children.Add(textGrid);
            memPanel.Children.Add(usageGrid);

            panel.Children.Add(CreateSettingsGroup("游戏内存", new[] { memPanel }));

            // Group 3: Advanced Options (Screenshot 4)
            var advItems = new List<UIElement>();
            _jvmArgsBox = CreateTextBox(string.IsNullOrEmpty(_customJvmArgs) ? "跟随全局设置" : _customJvmArgs, 60);
            _jvmArgsBox.TextChanged += (s, e) => { if (IsLoaded) _customJvmArgs = _jvmArgsBox.Text; };
            advItems.Add(CreateSettingItem("Java 虚拟机参数", _jvmArgsBox)); // Multiline?
            advItems.Add(CreateSettingItem("游戏参数", CreateTextBox("跟随全局设置")));
            advItems.Add(CreateSettingItem("启动前执行命令", CreateTextBox("")));
            
            // Checkboxes
            var cbStk = new StackPanel();
            cbStk.Children.Add(CreateCheckbox("禁止更新 Mod", false));
            cbStk.Children.Add(CreateCheckbox("忽略 Java 兼容性警告", false));
            cbStk.Children.Add(CreateCheckbox("关闭文件校验", false));
            cbStk.Children.Add(CreateCheckbox("禁用 Java Launch Wrapper", false));
            
            // Layout hack for checkboxes to align
             var cbGrid = new Grid { Margin = new Thickness(120, 0, 0, 0) };
             cbGrid.Children.Add(cbStk);
             advItems.Add(cbGrid);

            panel.Children.Add(CreateSettingsGroup("高级选项", advItems.ToArray()));

            // Global Settings Button (Bottom)
            var globalBtn = new Button
            {
                Content = "➜ 全局设置",
                Padding = new Thickness(20, 8, 20, 8),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 20),
                 Style = (Style)FindResource(typeof(Button))
            };
            globalBtn.Click += (s, e) => NavigationService.Navigate(new SettingsPage());
            panel.Children.Add(globalBtn);

            ContentArea.Children.Add(panel);
        }

        // --- Helpers ---
        private void ShowOverviewTab()
        {
            ContentArea.Children.Clear();
            ContentArea.Children.Add(CreateOverviewContent());
            TabOverview.IsEnabled = false;
        }

        private UIElement CreateOverviewContent()
        {
             var panel = new StackPanel();
            
            // Header
            var header = new Border 
            { 
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 255, 255)),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 20)
            };
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            
            // Icon
            var iconBorder = new Border { Width = 60, Height = 60, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 255, 255, 255)), CornerRadius = new CornerRadius(20), Margin = new Thickness(0,0,20,0) };
            iconBorder.Child = new TextBlock { Text = "🧊", FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            headerGrid.Children.Add(iconBorder);
            
            var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            textStack.Children.Add(new TextBlock { Text = _version.Id, FontSize = 24, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White });
            textStack.Children.Add(new TextBlock { Text = $"类型: {_version.Type} | 继承: {(_version.InheritsFrom.Count > 0 ? _version.InheritsFrom[0] : "无")}", Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 5, 0, 0) });
            
            Grid.SetColumn(textStack, 1);
            headerGrid.Children.Add(textStack);
            
            header.Child = headerGrid;
            panel.Children.Add(header);
            
            // Quick Actions
            panel.Children.Add(CreateSectionHeader("快速操作"));
            var actionsPanel = new WrapPanel();
            actionsPanel.Children.Add(CreateActionButton("▶ 启动游戏", (s, e) => LaunchSelectedVersion(), "#FF4CAF50"));
            actionsPanel.Children.Add(CreateActionButton("📂 打开目录", (s, e) => OpenFolder(_version.GameDir)));
            panel.Children.Add(actionsPanel);

            return panel;
        }
        
        private string GetModsPath() => System.IO.Path.Combine(_version.GameDir, "mods");

        private CheckBox CreateCheckbox(string text, bool isChecked, bool isEnabled = true)
        {
            return new CheckBox
            {
                Content = text,
                IsChecked = isChecked,
                IsEnabled = isEnabled,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            };
        }

        private RadioButton CreateRadioButton(string text, bool isChecked)
        {
            return new RadioButton
            {
                Content = text,
                IsChecked = isChecked,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 0, 15, 0)
            };
        }

        private TextBox CreateTextBox(string text, double height = 35)
        {
            return new TextBox
            {
                Text = text,
                Height = height,
                Padding = new Thickness(5),
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private ComboBox CreateComboBox(IEnumerable<string> items, int selectedIndex = 0)
        {
            return new ComboBox { ItemsSource = items, SelectedIndex = selectedIndex, Height = 35, VerticalContentAlignment = VerticalAlignment.Center };
        }
        
        private UIElement CreateRealMemorySlider()
        {
            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            var slider = new Slider 
            { 
                Minimum = 1024, 
                Maximum = 16384, 
                Value = _settings?.MaxMemoryMB ?? 4096, 
                Width = 300, 
                VerticalAlignment = VerticalAlignment.Center,
                IsSnapToTickEnabled = true,
                TickFrequency = 512
            };
            var label = new TextBlock 
            { 
                Text = $"{slider.Value} MB", 
                Margin = new Thickness(15, 0, 0, 0), 
                VerticalAlignment = VerticalAlignment.Center, 
                Foreground = System.Windows.Media.Brushes.White,
                MinWidth = 60
            };
            
            slider.ValueChanged += (s, e) => 
            {
                label.Text = $"{e.NewValue} MB";
                if (_settings != null) _settings.MaxMemoryMB = (int)e.NewValue;
            };
            
            stack.Children.Add(slider);
            stack.Children.Add(label);
            return stack;
        }

        private Border CreateSettingsGroup(string title, UIElement[] items)
        {
            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(20, 255, 255, 255)),
                CornerRadius = new CornerRadius(24), // Increased radius
                Padding = new Thickness(20),
                Margin = new Thickness(0, 0, 0, 20)
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 15) });
            foreach (var item in items) stack.Children.Add(item);
            border.Child = stack;
            return border;
        }

        private Grid CreateSettingItem(string label, UIElement control)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 15) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            
            grid.Children.Add(new TextBlock { Text = label, Foreground = System.Windows.Media.Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }

        private TextBlock CreateSectionHeader(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(0, 10, 0, 15)
            };
        }

        private UIElement CreateModItem(string modPath)
        {
             string fileName = System.IO.Path.GetFileName(modPath);
            bool isEnabled = modPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
            
            var border = new Border
            {
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromArgb(isEnabled ? (byte)20 : (byte)10, 255, 255, 255)),
                CornerRadius = new CornerRadius(20),
                Padding = new Thickness(15, 12, 15, 12),
                Margin = new Thickness(0, 0, 0, 6)
            };
            
             var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); 
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var toggle = new CheckBox 
            { 
                IsChecked = isEnabled, 
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            string capturedPath = modPath;
            toggle.Checked += (s, e) => ToggleMod(capturedPath, true);
            toggle.Unchecked += (s, e) => ToggleMod(capturedPath, false);
            grid.Children.Add(toggle);

            var nameText = new TextBlock
            {
                Text = fileName,
                FontSize = 14,
                Foreground = System.Windows.Media.Brushes.White,
                Opacity = isEnabled ? 1.0 : 0.5,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(nameText, 1);
            grid.Children.Add(nameText);
            
            border.Child = grid;
            return border;
        }

        private void ToggleMod(string modPath, bool enable)
        {
             try
            {
                string newPath;
                if (enable)
                    newPath = modPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? modPath.Substring(0, modPath.Length - ".disabled".Length) : modPath;
                else
                    newPath = modPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? modPath : modPath + ".disabled";

                if (newPath != modPath && System.IO.File.Exists(modPath))
                {
                    System.IO.File.Move(modPath, newPath);
                    ShowModsTab(); 
                }
            }
            catch {}
        }

        private string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " KB";
            return (bytes / 1024.0 / 1024.0).ToString("F1") + " MB";
        }

        private Button CreateActionButton(string text, RoutedEventHandler clickHandler, string bgColor = "#30FFFFFF")
        {
            var button = new Button
            {
                Content = text,
                Height = 40,
                Padding = new Thickness(20, 0, 20, 0),
                Margin = new Thickness(0, 0, 10, 10),
                Background = new System.Windows.Media.SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(bgColor)),
                Foreground = System.Windows.Media.Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 14,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += clickHandler;
            return button;
        }

        private void OpenFolder(string path)
        {
            try 
            {
                System.IO.Directory.CreateDirectory(path);
                System.Diagnostics.Process.Start("explorer.exe", path);
            }
            catch {}
        }
        private void JavaCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_settings == null || _javaCombo == null || !IsLoaded) return;

            if (_javaCombo.SelectedIndex == 2) // 自定义...
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Java Executable (javaw.exe;java.exe)|javaw.exe;java.exe|All Files (*.*)|*.*",
                    Title = "选择 Java 可执行文件"
                };
                if (dialog.ShowDialog() == true)
                {
                    _settings.JavaPath = dialog.FileName;
                    _settings.MemoryMode = MemoryAllocation.Custom;
                }
                else
                {
                    _javaCombo.SelectedIndex = 0; // revert
                }
            }
            else if (_javaCombo.SelectedIndex == 1) // 智能匹配
            {
                _settings.JavaPath = string.Empty;
                _settings.MemoryMode = MemoryAllocation.Custom;
            }
            else // 跟随全局设置
            {
                _settings.JavaPath = string.Empty;
                _settings.MemoryMode = MemoryAllocation.FollowGlobal;
            }
        }

        private void LaunchSelectedVersion()
        {
            SaveVersionSettings();

            if (Application.Current.MainWindow is MainWindow mw && mw.DataContext is ViewModels.MainViewModel vm)
            {
                var match = vm.GameVersions.FirstOrDefault(v => v.Id == _version.Id);
                if (match != null)
                {
                    vm.SelectedVersion = match;
                    if (vm.LaunchGameCommand.CanExecute(null))
                        vm.LaunchGameCommand.Execute(null);
                    return;
                }
            }
            MessageBox.Show("未找到对应版本，请返回主页选择版本", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private async Task DeleteVersionAsync()
        {
            if (MessageBox.Show($"确定要删除版本 \"{_version.Id}\" 吗？此操作不可恢复！",
                    "删除版本", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            try
            {
                await Task.Run(() =>
                {
                    string versionDir = Path.Combine(_version.RootPath, "versions", _version.Id);
                    if (Directory.Exists(versionDir))
                        Directory.Delete(versionDir, true);
                });
                MessageBox.Show("版本已删除", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
                if (NavigationService.CanGoBack) NavigationService.GoBack();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"删除失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string GetString(string key)
        {
            if (FindResource(key) is string value)
            {
                return value;
            }
            return $"[{key}]";
        }

    }
}
