using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeminiLauncher.Models;
using GeminiLauncher.Services;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GeminiLauncher.Controls;

namespace GeminiLauncher.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly GameService _gameService;
        private readonly LaunchService _launchService;
        private readonly ConfigService _configService;

        public ConfigService ConfigService => _configService;
        public NotificationService NotificationService { get; }
        public GeminiLauncher.Services.Network.DownloadManagerService DownloadManager => GeminiLauncher.Services.Network.DownloadManagerService.Instance;

        public event Action<int, string>? PreloadProgressChanged;
        public event Action? PreloadCompleted;

        [ObservableProperty]
        private string _title = "LYZL";

        [ObservableProperty]
        private AccountManager _accountManager;

        public ObservableCollection<GameInstance> GameVersions { get; } = new ObservableCollection<GameInstance>();

        [ObservableProperty]
        private GameInstance? _selectedVersion;

        [ObservableProperty]
        private string _statusMessage = "Ready to Launch";

        [ObservableProperty]
        private bool _isLaunching = false;

        [ObservableProperty]
        private ImageSource? _backgroundImage;

        [ObservableProperty]
        private double _backgroundOpacity = 0.6;

        [ObservableProperty]
        private double _blurEffectRadius = 0;

        [ObservableProperty]
        private bool _isGlobalResourcesOverlayActive = false;

        [ObservableProperty]
        private bool _isLoadingBackground = false;

        [ObservableProperty]
        private bool _isPreloading = true;

        private readonly JavaService _javaService;

        private string GetString(string key)
        {
            if (Application.Current.TryFindResource(key) is string s)
            {
                return s;
            }
            return $"[{key}]";
        }

        public MainViewModel()
        {
            _configService = ConfigService.Instance;
            _accountManager = new AccountManager();
            _gameService = new GameService(_configService);
            NotificationService = new NotificationService();
            _launchService = new LaunchService(NotificationService, _configService);
            _javaService = new JavaService();

            _statusMessage = GetString("Status_Ready");
            ApplyLanguage();

            _ = RunPreloadAsync();
        }

        private async Task RunPreloadAsync()
        {
            IsPreloading = true;

            BackgroundOpacity = _configService.Settings.BackgroundOpacity;
            BlurEffectRadius = _configService.Settings.BlurEffectRadius;

            try
            {
                await LoadBackgroundAsync();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load background failed: {ex.Message}");
                BackgroundImage = PreloadService.LoadDefaultBackground();
            }

            PreloadService.ProgressChanged += (s, e) =>
            {
                int progress = (int)(e.OverallProgress * 100);
                PreloadProgressChanged?.Invoke(progress, e.CurrentTask);
            };

            try
            {
                await PreloadService.PreloadAllAsync();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Preload failed: {ex.Message}");
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                IsPreloading = false;
                PreloadCompleted?.Invoke();
            });
        }

        private void ApplyLanguage()
        {
            if (!string.IsNullOrEmpty(_configService.Settings.Language))
            {
                App.SwitchLanguage(_configService.Settings.Language);
            }
        }

        public async Task LoadBackgroundAsync()
        {
            IsLoadingBackground = true;
            try
            {
                string? path = _configService.Settings.BackgroundImagePath;
                BackgroundOpacity = _configService.Settings.BackgroundOpacity;
                BlurEffectRadius = _configService.Settings.BlurEffectRadius;

                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    byte[] imageData = await Task.Run(() => System.IO.File.ReadAllBytesAsync(path));
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 1920;
                    bitmap.StreamSource = new System.IO.MemoryStream(imageData);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    BackgroundImage = bitmap;
                }
                else
                {
                    var bitmap = new BitmapImage(new System.Uri("pack://application:,,,/Assets/Images/cirno_bg.png"));
                    bitmap.Freeze();
                    BackgroundImage = bitmap;
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading background: {ex.Message}");
                var bitmap = new BitmapImage(new System.Uri("pack://application:,,,/Assets/Images/cirno_bg.png"));
                bitmap.Freeze();
                BackgroundImage = bitmap;
            }
            finally
            {
                IsLoadingBackground = false;
            }
        }

        [RelayCommand]
        private async Task PickBackgroundImageAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Background Image",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp;*.webp"
            };

            if (dialog.ShowDialog() == true)
            {
                _configService.Settings.BackgroundImagePath = dialog.FileName;
                _configService.SaveConfig();
                await LoadBackgroundAsync();
            }
        }
        
        public async Task LoadVersionsAsync()
        {
            await Task.Run(() =>
            {
                string dotMinecraft = _configService.Settings.GamePath;
                if (string.IsNullOrEmpty(dotMinecraft) || !System.IO.Directory.Exists(dotMinecraft))
                {
                     dotMinecraft = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), ".minecraft");
                }
                
                _configService.Settings.GamePath = dotMinecraft;
                _configService.SaveConfig();

                if (!System.IO.Directory.Exists(dotMinecraft))
                {
                    try { System.IO.Directory.CreateDirectory(dotMinecraft); } catch {}
                }
                
                var versions = _gameService.ScanVersions(dotMinecraft, _configService.Settings.VersionIsolation);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    string? prevSelectedId = SelectedVersion?.Id;
                    GameVersions.Clear();
                    foreach(var v in versions) GameVersions.Add(v);
                    
                    if (prevSelectedId != null)
                    {
                        var match = GameVersions.FirstOrDefault(v => v.Id == prevSelectedId);
                        SelectedVersion = match ?? GameVersions.FirstOrDefault();
                    }
                    else if (GameVersions.Any())
                    {
                        SelectedVersion = GameVersions.First();
                    }
                });
            });
        }

        [RelayCommand]
        private async Task LaunchGame()
        {
            if (SelectedVersion == null) { iOS26Dialog.Show(GetString("Msg_NoVersion")); return; }
            if (AccountManager.CurrentAccount == null) { iOS26Dialog.Show(GetString("Msg_NoAccount")); return; }

            string versionJsonPath = System.IO.Path.Combine(SelectedVersion.RootPath, "versions", SelectedVersion.Id, $"{SelectedVersion.Id}.json");
            if (!System.IO.File.Exists(versionJsonPath))
            {
                iOS26Dialog.Show($"版本 \"{SelectedVersion.Id}\" 的文件已不存在，可能已被外部删除。\n请刷新版本列表。", "版本缺失", DialogIcon.Warning);
                await LoadVersionsAsync();
                return;
            }

            // Resolve Java Path
            string javaPath = SelectedVersion.CustomJavaPath;

            // 1. Smart Selection: If no custom path, try to find the best matching Java version automatically
            if (string.IsNullOrWhiteSpace(javaPath))
            {
                // Pass the required version (e.g. 8 or 17) to find a specific match
                var bestMatch = _javaService.AutoDetectBestJava(SelectedVersion.RequiredJavaVersion);
                if (!string.IsNullOrEmpty(bestMatch))
                {
                   javaPath = bestMatch;
                }
            }

            // 2. Fallback to Global if auto-detect didn't find a specific match
            if (string.IsNullOrWhiteSpace(javaPath))
            {
                javaPath = _configService.Settings.JavaPath;
            }

            // 3. Final Check: If still missing or invalid, try to find ANY Java (last resort)
            if (string.IsNullOrWhiteSpace(javaPath) || !System.IO.File.Exists(javaPath))
            {
                var detected = _javaService.AutoDetectBestJava(0); 
                if (detected != null)
                {
                    javaPath = detected;
                    // Only save to Global if Global was empty
                    if (string.IsNullOrWhiteSpace(_configService.Settings.JavaPath))
                    {
                        _configService.Settings.JavaPath = javaPath;
                        _configService.SaveConfig();
                    }
                }
                else
                {
                    iOS26Dialog.Show(string.Format(GetString("Msg_JavaMissing"), SelectedVersion.RequiredJavaVersion), "未找到 Java", DialogIcon.Warning);
                    return;
                }
            }
            
            try 
            {
               IsLaunching = true;
               StatusMessage = GetString("Status_Checking");

               // Pre-launch checks
               var analyzer = new CrashAnalyzerService();
               string? conflictWarning = await analyzer.CheckForConflictsAsync(SelectedVersion.GameDir);
               if (conflictWarning != null)
               {
                   if (iOS26Dialog.Show($"潜在模组冲突:\n{conflictWarning}\n\n是否继续？", GetString("Title_Warning"), DialogIcon.Warning, DialogButtons.YesNo) != true)
                   {
                       IsLaunching = false;
                       StatusMessage = GetString("Status_Ready");
                       return;
                   }
               }

               var process = await _launchService.LaunchGameAsync(SelectedVersion, AccountManager.CurrentAccount, _configService.Settings.MaxRam, javaPath, 
                   (status) => 
                   {
                       // Update status from LaunchService
                       Application.Current.Dispatcher.Invoke(() => StatusMessage = status);
                   },
                   (progress) => 
                   {
                       // Update progress (if needed)
                       Application.Current.Dispatcher.Invoke(() => 
                       {
                           // You can use this progress value to update a progress bar or other UI elements
                       });
                   });

               if (process != null)
               {
                   // Apply Launcher Visibility
                   int visibility = _configService.Settings.LauncherVisibility;
                   if (visibility == 2) // Close
                   {
                       Application.Current.Shutdown();
                       return;
                   }
                   
                   if (visibility == 1) // Hide
                   {
                       Application.Current.MainWindow.Hide();
                   }

                   StatusMessage = GetString("Status_Running");
                   await process.WaitForExitAsync();
                   
                   // Restore Visibility if Hidden
                   if (visibility == 1)
                   {
                       Application.Current.MainWindow.Show();
                       Application.Current.MainWindow.WindowState = WindowState.Normal;
                       Application.Current.MainWindow.Activate();
                   }

                   IsLaunching = false;
                   StatusMessage = GetString("Status_Ready");

                   if (process.ExitCode != 0)
                   {
                       var crashAnalyzer = new CrashAnalyzerService();
                       var result = await crashAnalyzer.AnalyzeAsync(SelectedVersion.GameDir);
                       
                       if (result.IsCrashDetected)
                       {
                           iOS26Dialog.Show($"游戏崩溃！\n\n原因: {result.Cause}\n解决方案: {result.Solution}", GetString("Title_Crash"), DialogIcon.Error);
                       }
                       else
                       {
                            iOS26Dialog.Show($"游戏退出，退出码: {process.ExitCode}。请查看日志获取详情。", GetString("Title_GameExited"), DialogIcon.Warning);
                       }
                   }
               }
               else
               {
                   // Process failed to start: restore UI state so the user can retry
                   IsLaunching = false;
                   StatusMessage = GetString("Status_Ready");
               }
            }
            catch (System.Exception ex)
            {
                // Ensure window is shown if launch fails
                Application.Current.MainWindow.Show();
                
                IsLaunching = false;
                StatusMessage = GetString("Status_Failed");
                iOS26Dialog.Show($"启动失败: {ex.Message}", "错误", DialogIcon.Error);
            }
        }
        [RelayCommand]
        private async Task CheckUpdate()
        {
            var updateService = new UpdateService();
            await updateService.CheckForUpdatesAsync();
        }

        [RelayCommand]
        private async Task ExportModpack()
        {
            if (SelectedVersion == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Modrinth Modpack (*.mrpack)|*.mrpack",
                FileName = $"{SelectedVersion.Id}.mrpack",
                Title = "Export Modpack"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var modpackService = new GeminiLauncher.Services.Ecosystem.ModpackService();
                    await modpackService.ExportMrPackAsync(SelectedVersion, dialog.FileName);
                    iOS26Dialog.Show(GetString("Title_ExportComplete"), "导出完成", DialogIcon.Success);
                }
                catch (System.Exception ex)
                {
                    iOS26Dialog.Show($"导出失败: {ex.Message}", "错误", DialogIcon.Error);
                }
            }
        }
        [RelayCommand]
        private async Task UploadLog()
        {
            if (SelectedVersion == null) return;
            string logPath = System.IO.Path.Combine(SelectedVersion.GameDir, "logs", "latest.log");

            if (!System.IO.File.Exists(logPath))
            {
                iOS26Dialog.Show(GetString("Msg_LogNotFound"), GetString("Title_Warning"), DialogIcon.Info);
                return;
            }

            try
            {
                iOS26Dialog.Show(GetString("Msg_UploadNotImplemented"), GetString("Title_Warning"), DialogIcon.Info);
                await Task.CompletedTask;
            }
            catch (System.Exception ex)
            {
                iOS26Dialog.Show($"上传失败: {ex.Message}", "错误", DialogIcon.Error);
            }
        }
        public event System.Action<object>? RequestNavigation;
        public event System.Action? RequestGoBack;

        [RelayCommand]
        private void OpenLaunchSettings()
        {
            if (SelectedVersion == null) return;
            // Triggers navigation in MainWindow
            RequestNavigation?.Invoke(new GeminiLauncher.Views.VersionSettingsPage(SelectedVersion));
        }

        [RelayCommand]
        private void SaveVersionSettings()
        {
            if (SelectedVersion != null)
            {
                _gameService.SaveVersionConfig(SelectedVersion);
            }
            RequestGoBack?.Invoke();
        }

        [RelayCommand]
        private void NavigateBack()
        {
            RequestGoBack?.Invoke();
        }
    }
}
