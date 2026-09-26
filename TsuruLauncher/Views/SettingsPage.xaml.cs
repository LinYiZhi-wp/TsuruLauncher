using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using System.IO;
using System.Linq;
using TsuruLauncher.ViewModels;
using TsuruLauncher.Controls;

namespace TsuruLauncher.Views
{
    public partial class SettingsPage : Page
    {
        public SettingsPage()
        {
            InitializeComponent();
            // Use MainViewModel from Application
            this.DataContext = ((App)Application.Current).MainWindow.DataContext;

            // 自检钩子：TSURU_SELFTEST=accounts 时跑「导出 → 导入」往返
            if (Environment.GetEnvironmentVariable("TSURU_SELFTEST") == "accounts")
                Dispatcher.BeginInvoke(new Action(RunAccountsSelfTest),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            
            // Initialize fields from Config
            if (DataContext is ViewModels.MainViewModel vm)
            {
                var cfg = vm.ConfigService.Settings;

                if (!string.IsNullOrEmpty(cfg.GamePath))
                    GamePathBox.Text = cfg.GamePath;

                if (!string.IsNullOrEmpty(cfg.JavaPath))
                {
                   JavaPathBox.Text = cfg.JavaPath;
                   UpdateJavaVersionInfo(cfg.JavaPath);
                }

                // Restore memory slider
                MemorySlider.Value = cfg.MaxRam > 0 ? cfg.MaxRam : 4096;

                // Restore Auto/Custom memory mode
                if (cfg.AutoDetectMemory)
                {
                    AutoMemoryRadio.IsChecked = true;
                    CustomMemoryRadio.IsChecked = false;
                    MemorySlider.IsEnabled = false;
                }
                else
                {
                    AutoMemoryRadio.IsChecked = false;
                    CustomMemoryRadio.IsChecked = true;
                    MemorySlider.IsEnabled = true;
                }

                // Restore version isolation toggle
                VersionIsolationToggle.IsChecked = cfg.VersionIsolation;

                // Restore language selection
                if (cfg.Language == "zh-CN")
                    ChineseRadio.IsChecked = true;
                else
                    EnglishRadio.IsChecked = true;

                // Restore Download Source
                foreach (ComboBoxItem item in DownloadSourceCombo.Items)
                {
                    if (item.Tag?.ToString() == cfg.DownloadSource)
                    {
                        DownloadSourceCombo.SelectedItem = item;
                        break;
                    }
                }

                // Restore Hidden Pages
                if (cfg.HiddenPageKeys.Contains("download")) HideDownloadCheck.IsChecked = true;
                if (cfg.HiddenPageKeys.Contains("settings")) HideSettingsCheck.IsChecked = true;

                // Restore appearance controls (handlers ignore this initial pass)
                AccentList.ItemsSource = Services.ThemeService.AccentPresets;
                RefreshThemeControls();
            }
            
            // Initialize memory slider value display
            UpdateMemoryValueText();

            // Initialize PCL-style memory settings
            UpdateSystemMemoryInfo();
            UpdateMemorySlidersState();
        }

        /// <summary>Pushes the saved mode/accent into the controls (no side effects).</summary>
        private void RefreshThemeControls()
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;
            var cfg = vm.ConfigService.Settings;

            var mode = Services.ThemeService.ParseMode(cfg.ThemeMode);
            ModeLightRadio.IsChecked = mode == Services.ThemeMode.Light;
            ModeDarkRadio.IsChecked = mode == Services.ThemeMode.Dark;
            ModeOledRadio.IsChecked = mode == Services.ThemeMode.Oled;

            bool followSystem = string.Equals(cfg.AccentColor, Services.ThemeService.SystemAccentKey,
                StringComparison.OrdinalIgnoreCase);
            SystemAccentCheck.IsChecked = followSystem;

            if (!followSystem && cfg.AccentColor.StartsWith("#", StringComparison.Ordinal))
            {
                CustomAccentBox.Text = cfg.AccentColor;
                AccentList.SelectedItem = null;
            }
            else
            {
                AccentList.SelectedItem = Services.ThemeService.AccentPresets
                    .FirstOrDefault(a => string.Equals(a.Key, cfg.AccentColor, StringComparison.OrdinalIgnoreCase))
                    ?? Services.ThemeService.AccentPresets[0];
            }
        }

        /// <summary>Applies the current control state and persists it.</summary>
        private void ApplyThemeSettings()
        {
            if (!IsLoaded) return;
            if (DataContext is not ViewModels.MainViewModel vm) return;

            string mode = ModeLightRadio.IsChecked == true ? "light"
                        : ModeOledRadio.IsChecked == true ? "oled"
                        : "dark";
            string accent = SystemAccentCheck.IsChecked == true
                ? Services.ThemeService.SystemAccentKey
                : (AccentList.SelectedItem as Services.AccentSwatch)?.Key ?? "green";

            Services.ThemeService.Apply(mode, accent);

            vm.ConfigService.Settings.ThemeMode = mode;
            vm.ConfigService.Settings.AccentColor = accent;
            vm.ConfigService.SaveConfig();
        }

        private async void DownloadJavaRuntime_Click(object sender, RoutedEventArgs e)
        {
            if (JavaRuntimeVersionCombo.SelectedItem is not ComboBoxItem item) return;
            if (!int.TryParse(item.Tag?.ToString(), out int major)) return;

            JavaRuntimeDownloadBtn.IsEnabled = false;
            JavaRuntimeProgressBar.Visibility = Visibility.Visible;
            JavaRuntimeProgressBar.Value = 0;
            JavaRuntimeStatusText.Text = $"正在准备 Java {major}...";

            try
            {
                var progress = new Progress<double>(p => JavaRuntimeProgressBar.Value = p * 100);
                var status = new Progress<string>(s => JavaRuntimeStatusText.Text = s);

                string javaExe = await Services.JavaRuntimeService.EnsureAsync(major, progress, status);

                new Services.JavaService().ClearCache();
                JavaRuntimeStatusText.Text = $"已就绪：{javaExe}";

                // adopt it as the global runtime
                if (DataContext is ViewModels.MainViewModel vm)
                {
                    vm.ConfigService.Settings.JavaPath = javaExe;
                    vm.ConfigService.SaveConfig();
                }
                JavaPathBox.Text = javaExe;
                UpdateJavaVersionInfo(javaExe);
            }
            catch (System.Exception ex)
            {
                JavaRuntimeStatusText.Text = "失败：" + ex.Message;
                iOS26Dialog.Show($"Java 下载失败：\n{ex.Message}", "错误", DialogIcon.Error);
            }
            finally
            {
                JavaRuntimeDownloadBtn.IsEnabled = true;
                JavaRuntimeProgressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void ThemeMode_Checked(object sender, RoutedEventArgs e) => ApplyThemeSettings();

        private void AccentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (AccentList.SelectedItem != null)
            {
                CustomAccentBox.Text = string.Empty;
                if (SystemAccentCheck != null) SystemAccentCheck.IsChecked = false;
            }
            ApplyThemeSettings();
        }

        private void SystemAccent_Changed(object sender, RoutedEventArgs e)
        {
            if (SystemAccentCheck.IsChecked == true)
            {
                AccentList.SelectedItem = null;
                CustomAccentBox.Text = string.Empty;
            }
            ApplyThemeSettings();
        }

        private void ApplyCustomAccent_Click(object sender, RoutedEventArgs e)
        {
            string hex = (CustomAccentBox.Text ?? string.Empty).Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(hex, "^#?[0-9a-fA-F]{6}$"))
            {
                iOS26Dialog.Show("请输入有效的颜色值，例如 #7C5CFF", "格式错误", DialogIcon.Warning);
                return;
            }
            if (!hex.StartsWith("#")) hex = "#" + hex;

            if (DataContext is not ViewModels.MainViewModel vm) return;
            string mode = ModeLightRadio.IsChecked == true ? "light"
                        : ModeOledRadio.IsChecked == true ? "oled"
                        : "dark";

            Services.ThemeService.Apply(mode, hex);
            vm.ConfigService.Settings.ThemeMode = mode;
            vm.ConfigService.Settings.AccentColor = hex;
            vm.ConfigService.SaveConfig();

            SystemAccentCheck.IsChecked = false;
            AccentList.SelectedItem = null;
            CustomAccentBox.Text = hex;
        }

        private void CurseForgeKey_Changed(object sender, TextChangedEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;
            vm.ConfigService.Settings.CurseForgeApiKey = (CurseForgeKeyBox.Text ?? string.Empty).Trim();
            vm.ConfigService.SaveConfig();
        }

        private void OpenCurseForgeConsole_Click(object sender, RoutedEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://console.curseforge.com/?#/api-keys") { UseShellExecute = true }); }
            catch { }
        }

        private void YggdrasilPreset_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (YggdrasilServerBox == null || YggdrasilPresetCombo == null) return;
            if (YggdrasilPresetCombo.SelectedItem is not ComboBoxItem item) return;
            string tag = item.Tag?.ToString() ?? string.Empty;
            if (!string.IsNullOrEmpty(tag)) YggdrasilServerBox.Text = tag;
        }

        private async void TestYggdrasilServer_Click(object sender, RoutedEventArgs e)
        {
            string server = YggdrasilServerBox.Text ?? string.Empty;
            try
            {
                YggdrasilStatusText.Text = "正在连接...";
                string name = await new Services.YggdrasilAuthService().GetServerNameAsync(server);
                YggdrasilStatusText.Text = "已连接：" + name;
                iOS26Dialog.Show("服务器可正常访问：" + name, "连接成功", DialogIcon.Success);
            }
            catch (System.Exception ex)
            {
                YggdrasilStatusText.Text = "无法连接";
                iOS26Dialog.Show("连接失败：" + ex.Message, "错误", DialogIcon.Error);
            }
        }

        private async void AddYggdrasilAccount_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;

            string server = YggdrasilServerBox.Text ?? string.Empty;
            string user = YggdrasilUserBox.Text ?? string.Empty;
            string pass = YggdrasilPassBox.Password ?? string.Empty;

            try
            {
                YggdrasilStatusText.Text = "正在登录...";
                await vm.AccountManager.LoginYggdrasil(server, user, pass);
                YggdrasilStatusText.Text = "登录成功";
                YggdrasilPassBox.Password = string.Empty;
                iOS26Dialog.Show("外置登录成功，已切换到该账号。", "成功", DialogIcon.Success);
            }
            catch (System.Exception ex)
            {
                YggdrasilStatusText.Text = "登录失败";
                iOS26Dialog.Show("外置登录失败：" + ex.Message, "错误", DialogIcon.Error);
            }
        }

        private void ShowLaunchLog_Changed(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;
            vm.ConfigService.Settings.ShowLaunchLog = ShowLaunchLogToggle.IsChecked == true;
            vm.ConfigService.SaveConfig();
        }

        private void OpenLaunchLog_Click(object sender, RoutedEventArgs e)
        {
            var vm = DataContext as ViewModels.MainViewModel;
            Views.LaunchLogWindow.ShowWindow(vm?.ConfigService.Settings.LastSelectedVersionId ?? "Tsuru Launcher");
        }

        private void AddOfflineAccount_Click(object sender, RoutedEventArgs e)
        {
            var username = OfflineUsernameBox.Text;
            if (!string.IsNullOrWhiteSpace(username))
            {
                var vm = DataContext as ViewModels.MainViewModel;
                vm?.AccountManager.LoginOffline(username);
                OfflineUsernameBox.Text = string.Empty;
            }
        }

        private async void AddMicrosoftAccount_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var vm = DataContext as MainViewModel;
                if (vm == null) return;
                await vm.AccountManager.LoginMicrosoft();
            }
            catch (System.Exception ex)
            {
                iOS26Dialog.Show($"Login Failed: {ex.Message}", "错误", DialogIcon.Error);
            }
        }

        /// <summary>
        /// 自检：TSURU_SELFTEST=accounts 时跑一次「导出 → 导入」往返，
        /// 验证导出的文件能被重新解析、账号数量对得上、重复导入不会重复添加。
        /// </summary>
        private void RunAccountsSelfTest()
        {
            try
            {
                if (DataContext is not MainViewModel vm) { Log("❌ 无 DataContext"); return; }

                int before = vm.AccountManager.Accounts.Count;
                string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                    $"tsuru-accounts-selftest-{Guid.NewGuid():N}.json");

                Log($"往返测试开始：现有 {before} 个账号");

                if (!vm.ExportAccounts(tmp)) { Log("❌ 导出失败"); return; }
                Log($"✅ 导出成功：{new System.IO.FileInfo(tmp).Length} 字节");

                var root = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(tmp));
                int inFile = (root["accounts"] as Newtonsoft.Json.Linq.JArray)?.Count ?? -1;
                Log($"✅ 文件里解析到 {inFile} 个账号（应为 {before}）");

                var (added, skipped) = vm.ImportAccounts(tmp);
                Log($"✅ 重复导入：新增 {added}，跳过 {skipped}（期望 0 / {before}）");

                int after = vm.AccountManager.Accounts.Count;
                Log(after == before ? $"✅ 往返后账号数不变（{after}）" : $"❌ 账号数变了：{before} -> {after}");

                try { System.IO.File.Delete(tmp); } catch { }
            }
            catch (Exception ex) { Log("❌ 自检异常：" + ex.Message); }
        }

        private static void Log(string msg)
            => Utilities.Logger.LogInfo("[AccountSelfTest] " + msg);

        /// <summary>
        /// 导出全部账号。⚠ 导出的文件里**含登录令牌**，要明确提醒用户。
        /// </summary>
        private void ExportAccounts_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            if (vm.AccountManager.Accounts.Count == 0)
            {
                iOS26Dialog.Show("当前没有可导出的账号。", "导出账号", DialogIcon.Info);
                return;
            }

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "导出账号",
                FileName = $"TsuruLauncher-accounts-{DateTime.Now:yyyyMMdd}.json",
                Filter = "账号备份 (*.json)|*.json",
            };
            if (dialog.ShowDialog() != true) return;

            if (!vm.ExportAccounts(dialog.FileName))
            {
                iOS26Dialog.Show("导出失败，详见日志。", "导出账号", DialogIcon.Error);
                return;
            }

            // ⚠ 必须说清楚：这个文件等同于账号密码，能直接拿去登录
            iOS26Dialog.Show(
                $"已导出 {vm.AccountManager.Accounts.Count} 个账号。\n\n" +
                "⚠ 这个文件里包含登录令牌，等同于账号凭证 ——\n" +
                "请妥善保管，不要发给别人。",
                "导出成功", DialogIcon.Warning);
        }

        /// <summary>
        /// 导入账号：**按 Uuid 合并**，已存在的跳过，不会清掉现有账号。
        /// </summary>
        private void ImportAccounts_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "导入账号",
                Filter = "账号备份 (*.json)|*.json|所有文件 (*.*)|*.*",
            };
            if (dialog.ShowDialog() != true) return;

            var (added, skipped) = vm.ImportAccounts(dialog.FileName);

            if (added == 0 && skipped == 0)
            {
                iOS26Dialog.Show("这个文件里没有解析到账号。", "导入账号", DialogIcon.Warning);
                return;
            }

            iOS26Dialog.Show(
                $"导入完成：新增 {added} 个，跳过 {skipped} 个（已存在）。",
                "导入账号", DialogIcon.Success);
        }

        private void RemoveAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is TsuruLauncher.Models.Account account)
            {
                if (iOS26Dialog.Show($"确定要删除账号 '{account.Username}' 吗？",
                    "确认删除", DialogIcon.Warning, DialogButtons.YesNo) == true)
                {
                    if (DataContext is MainViewModel vm)
                    {
                        vm.AccountManager.RemoveAccount(account);
                    }
                }
            }
        }

        private void BrowseGamePath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Minecraft folder (choose any file in the folder)",
                ValidateNames = false,
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Folder Selection"
            };

            if (dialog.ShowDialog() == true)
            {
                var folderPath = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(folderPath))
                {
                    GamePathBox.Text = folderPath;
                    if (DataContext is ViewModels.MainViewModel vm)
                    {
                        vm.ConfigService.Settings.GamePath = folderPath;
                        vm.ConfigService.SaveConfig();
                    }
                }
            }
        }

        private void BrowseJavaPath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Java Executable (javaw.exe;java.exe)|javaw.exe;java.exe|All Files (*.*)|*.*",
                Title = "Select Java Executable"
            };

            if (dialog.ShowDialog() == true)
            {
                string path = dialog.FileName;
                JavaPathBox.Text = path;
                UpdateJavaVersionInfo(path);
                
                if (DataContext is ViewModels.MainViewModel vm)
                {
                    vm.ConfigService.Settings.JavaPath = path;
                    vm.ConfigService.SaveConfig();
                }
            }
        }

        private void JavaAutoSearchCombo_DropDownOpened(object? sender, EventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo == null) return;

            var javaService = new TsuruLauncher.Services.JavaService();
            javaService.ClearCache();
            var installations = javaService.FindInstallations();

            combo.Items.Clear();
            if (installations.Any())
            {
                foreach (var inst in installations)
                {
                    string bitTag = inst.Is64Bit ? "64-bit" : "32-bit";
                    var item = new ComboBoxItem
                    {
                        Content = $"{inst.Version} ({bitTag}) — {inst.Path}",
                        Tag = inst
                    };
                    combo.Items.Add(item);
                }
            }
            else
            {
                var item = new ComboBoxItem
                {
                    Content = "未找到 Java 安装",
                    IsEnabled = false
                };
                combo.Items.Add(item);
            }
        }

        private void JavaAutoSearchCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var combo = sender as ComboBox;
            if (combo?.SelectedItem is not ComboBoxItem item) return;
            if (item.Tag is not TsuruLauncher.Services.JavaInstallation inst) return;

            JavaPathBox.Text = inst.Path;
            UpdateJavaVersionInfo(inst.Path);

            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.ConfigService.Settings.JavaPath = inst.Path;
                vm.ConfigService.SaveConfig();
            }
        }

        private void UpdateJavaVersionInfo(string javaPath)
        {
            try
            {
                var javaService = new TsuruLauncher.Services.JavaService();
                var info = javaService.GetJavaInfo(javaPath);
                if (info != null)
                {
                    string bitTag = info.Is64Bit ? "64-bit" : "32-bit";
                    JavaVersionText.Text = $"✓ {info.Version} ({bitTag})";
                }
                else
                {
                    JavaVersionText.Text = "✓ Java detected";
                }

                JavaVersionText.Foreground = Utilities.ThemeBrush.Success;
            }
            catch
            {
                JavaVersionText.Text = "⚠️ Unable to detect version";
                JavaVersionText.Foreground = Utilities.ThemeBrush.Warning;
            }
        }

        private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateMemoryValueText();

            // Persist memory setting
            if (IsLoaded && DataContext is ViewModels.MainViewModel vm)
            {
                vm.ConfigService.Settings.MaxRam = (int)MemorySlider.Value;
                vm.ConfigService.SaveConfig();
            }
        }

        private void AutoMemoryRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.ConfigService.Settings.AutoDetectMemory = true;
                MemorySlider.IsEnabled = false;

                // Auto-detect and set recommended memory
                long totalMem = TsuruLauncher.Services.LaunchService.GetTotalSystemMemoryMB();
                long recommended = totalMem * 50 / 100;
                recommended = Math.Max(1024, Math.Min(recommended, totalMem - 1024));
                MemorySlider.Value = Math.Max(2048, recommended);
                vm.ConfigService.Settings.MaxRam = (int)MemorySlider.Value;
                vm.ConfigService.SaveConfig();
                UpdateMemoryValueText();
            }
        }

        private void CustomMemoryRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.ConfigService.Settings.AutoDetectMemory = false;
                MemorySlider.IsEnabled = true;
                vm.ConfigService.SaveConfig();
                UpdateMemoryValueText();
            }
        }

        private void UpdateMemoryValueText()
        {
            if (MemoryValueText != null && MemorySlider != null)
            {
                int valueMB = (int)MemorySlider.Value;
                bool isAuto = AutoMemoryRadio.IsChecked == true;
                string modeLabel = isAuto ? " (自动)" : " (自定义)";
                MemoryValueText.Text = $"{valueMB} MB{modeLabel}";

                // Smart memory warning
                if (MemoryWarningText != null)
                {
                    try
                    {
                        long totalPhysicalMB = TsuruLauncher.Services.LaunchService.GetTotalSystemMemoryMB();
                        double ratio = (double)valueMB / totalPhysicalMB;

                        if (valueMB < 1024)
                        {
                            MemoryWarningText.Text = "⚠️ 分配过低，可能导致游戏崩溃";
                            MemoryWarningText.Foreground = Utilities.ThemeBrush.Danger;
                        }
                        else if (ratio > 0.8)
                        {
                            MemoryWarningText.Text = $"⚠️ 超过物理内存 80%（{totalPhysicalMB} MB），可能导致卡死";
                            MemoryWarningText.Foreground = Utilities.ThemeBrush.Danger;
                        }
                        else
                        {
                            MemoryWarningText.Text = $"✓ 系统内存 {totalPhysicalMB} MB | 推荐范围";
                            MemoryWarningText.Foreground = Utilities.ThemeBrush.Success;
                        }
                    }
                    catch
                    {
                        MemoryWarningText.Text = "";
                    }
                }
            }
        }

        // ⚠ async void 抛异常会崩应用 —— 切换开关时重载版本可能失败，必须兜住
        private async void VersionIsolationToggle_Changed(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!IsLoaded) return;
                if (DataContext is ViewModels.MainViewModel vm)
                {
                    vm.ConfigService.Settings.VersionIsolation = VersionIsolationToggle.IsChecked == true;
                    vm.ConfigService.SaveConfig();

                    // Reload versions to update game directories
                    await vm.LoadVersionsAsync();
                }
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "切换版本隔离");
            }
        }

        private void Language_Changed(object sender, System.Windows.RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            
            if (DataContext is ViewModels.MainViewModel vm)
            {
                if (EnglishRadio.IsChecked == true)
                {
                    App.SwitchLanguage("en-US");
                    vm.ConfigService.Settings.Language = "en-US";
                }
                else if (ChineseRadio.IsChecked == true)
                {
                    App.SwitchLanguage("zh-CN");
                    vm.ConfigService.Settings.Language = "zh-CN";
                }
                vm.ConfigService.SaveConfig();
            }
        }

        private void BrowseBackgroundImage_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.PickBackgroundImageCommand.Execute(null);
            }
        }

        private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            if (DataContext is ViewModels.MainViewModel vm)
            {
                // ViewModel property bound TwoWay, just need to sync to config and save
                vm.ConfigService.Settings.BackgroundOpacity = vm.BackgroundOpacity;
                vm.ConfigService.SaveConfig();
            }
        }

        private void BlurSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.ConfigService.Settings.BlurEffectRadius = vm.BlurEffectRadius;
                vm.ConfigService.SaveConfig();
            }
        }

        private void LauncherVisibility_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.ConfigService.SaveConfig();
            }
        }

        private void ProcessPriority_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.ConfigService.SaveConfig();
            }
        }
        private void DownloadSource_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (DataContext is ViewModels.MainViewModel vm && DownloadSourceCombo.SelectedItem is ComboBoxItem item)
            {
                vm.ConfigService.Settings.DownloadSource = item.Tag?.ToString() ?? "Official";
                vm.ConfigService.SaveConfig();
            }
        }

        private void MaxThreads_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.ConfigService.SaveConfig();
            }
        }
        private void AdvancedSetting_Changed(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.ConfigService.SaveConfig();
            }
        }

        private void HidePage_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            if (DataContext is ViewModels.MainViewModel vm && sender is CheckBox chk && chk.Tag is string key)
            {
                if (chk.IsChecked == true)
                {
                    if (!vm.ConfigService.Settings.HiddenPageKeys.Contains(key))
                        vm.ConfigService.Settings.HiddenPageKeys.Add(key);
                }
                else
                {
                    vm.ConfigService.Settings.HiddenPageKeys.Remove(key);
                }

                vm.ConfigService.SaveConfig();

                // Notify MainWindow to refresh visibility
                if (Application.Current.MainWindow is MainWindow window)
                {
                    window.RefreshNavigationVisibility();
                }
            }
        }

        private void AutoDetectMemory_Changed(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded) return;
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.ConfigService.SaveConfig();
                UpdateSystemMemoryInfo();
                UpdateMemorySlidersState();
            }
        }

        private void MaxRam_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            if (DataContext is ViewModels.MainViewModel vm)
            {
                int maxRam = (int)MaxRamSlider.Value;
                int minRam = (int)MinRamSlider.Value;

                // Ensure min <= max
                if (minRam > maxRam)
                {
                    minRam = Math.Min(512, maxRam / 4);
                    MinRamSlider.Value = minRam;
                }

                vm.ConfigService.Settings.MaxRam = maxRam;
                vm.ConfigService.SaveConfig();
                UpdateSystemMemoryInfo();
            }
        }

        private void MinRam_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded) return;
            if (DataContext is ViewModels.MainViewModel vm)
            {
                int maxRam = (int)MaxRamSlider.Value;
                int minRam = (int)MinRamSlider.Value;

                // Ensure min <= max
                if (minRam > maxRam)
                {
                    minRam = maxRam;
                    MinRamSlider.Value = minRam;
                }

                vm.ConfigService.Settings.MinRam = minRam;
                vm.ConfigService.SaveConfig();
            }
        }

        private void UpdateSystemMemoryInfo()
        {
            try
            {
                long totalMem = TsuruLauncher.Services.LaunchService.GetTotalSystemMemoryMB();
                long recommended = totalMem * 50 / 100;
                recommended = Math.Max(1024, Math.Min(recommended, totalMem - 1024));

                SystemMemoryInfoText.Text = $"系统: {totalMem} MB | 推荐: {recommended} MB";
            }
            catch
            {
                SystemMemoryInfoText.Text = "";
            }
        }

        private void UpdateMemorySlidersState()
        {
            bool autoDetect = AutoDetectMemoryToggle.IsChecked == true;

            // When auto-detect is enabled, sliders are still visible but show recommended values
            // Users can still adjust them as upper limits
            MaxRamSlider.IsEnabled = true;
            MinRamSlider.IsEnabled = true;

            if (autoDetect)
            {
                try
                {
                    long totalMem = TsuruLauncher.Services.LaunchService.GetTotalSystemMemoryMB();
                    long recommended = totalMem * 50 / 100;
                    recommended = Math.Max(1024, Math.Min(recommended, totalMem - 1024));

                    // Set slider maximum to system memory
                    MaxRamSlider.Maximum = totalMem;

                    // If current value is 0 or default, set to recommended
                    if (MaxRamSlider.Value <= 0 || MaxRamSlider.Value == 4096)
                    {
                        MaxRamSlider.Value = (int)Math.Max(2048, recommended);
                    }
                }
                catch { }
            }
            else
            {
                MaxRamSlider.Maximum = 32768;
            }
        }

        private void ModrinthMirror_Changed(object sender, TextChangedEventArgs e)
        {
            if (!IsLoaded) return;
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.ConfigService.SaveConfig();
            }
        }

        private void ResetModrinthMirror_Click(object sender, RoutedEventArgs e)
        {
            const string defaultUrl = "https://api.modrinth.com/v2/";
            ModrinthMirrorBox.Text = defaultUrl;
            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.ConfigService.Settings.ModrinthApiBaseUrl = defaultUrl;
                vm.ConfigService.SaveConfig();
            }
        }
    }
}