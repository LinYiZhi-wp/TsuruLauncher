using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeminiLauncher.Models.Ecosystem;
using GeminiLauncher.Services.Ecosystem;
using GeminiLauncher.Services;
using GeminiLauncher.Services.Network;
using GeminiLauncher.Controls;

namespace GeminiLauncher.ViewModels
{
    public class LocalModFile : ObservableObject
    {
        private string _fileName = string.Empty;
        private string _filePath = string.Empty;
        private string _fileType = string.Empty;
        private long _fileSize;
        private bool _isEnabled = true;
        private System.Windows.Media.ImageSource? _previewImage;

        public string FileName { get => _fileName; set => SetProperty(ref _fileName, value); }
        public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }
        public string FileType { get => _fileType; set => SetProperty(ref _fileType, value); }
        public long FileSize { get => _fileSize; set => SetProperty(ref _fileSize, value); }
        public bool IsEnabled { get => _isEnabled; set => SetProperty(ref _isEnabled, value); }
        public System.Windows.Media.ImageSource? PreviewImage { get => _previewImage; set => SetProperty(ref _previewImage, value); }

        public string FileSizeDisplay
        {
            get
            {
                if (_fileSize < 1024) return $"{_fileSize} B";
                if (_fileSize < 1024 * 1024) return $"{_fileSize / 1024.0:F1} KB";
                return $"{_fileSize / (1024.0 * 1024.0):F1} MB";
            }
        }
    }

    public partial class ResourcesViewModel : ObservableObject
    {
        private readonly ModrinthService _modrinthService;
        private readonly ModpackService _modpackService;
        private readonly ConfigService _configService;
        private CancellationTokenSource? _searchDebounceCts;
        private CancellationTokenSource? _featuredLoadCts;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<ResourceDetail?>> _preloadCache = new();

        [ObservableProperty]
        private string _searchQuery = string.Empty;

        [ObservableProperty]
        private bool _isBusy = false;

        [ObservableProperty]
        private bool _isFeaturedLoading = true;

        [ObservableProperty]
        private bool _isImporting;

        [ObservableProperty]
        private double _importProgress;

        [ObservableProperty]
        private string _importStatus = "Preparing...";

        [ObservableProperty]
        private bool _hasSearchResults;

        [ObservableProperty]
        private string _selectedCategory = "mod";

        [ObservableProperty]
        private string? _selectedGameVersion;

        public ObservableCollection<string> GameVersions { get; } = new() { "1.21", "1.20", "1.19", "1.18", "1.17", "1.16", "1.12" };

        [ObservableProperty]
        private bool _isFullPageView;

        [ObservableProperty]
        private bool _isSidebarCollapsed = true;

        [ObservableProperty]
        private bool _isSearchEmpty;

        [ObservableProperty]
        private bool _isLocalModPanelOpen;

        [ObservableProperty]
        private LocalModFile? _selectedLocalMod;

        public ObservableCollection<ModProject> SearchResults { get; } = new ObservableCollection<ModProject>();
        public ObservableCollection<ModProject> TrendingMods { get; } = new ObservableCollection<ModProject>();
        public ObservableCollection<ModProject> NewestMods { get; } = new ObservableCollection<ModProject>();
        public ObservableCollection<LocalModFile> LocalMods { get; } = new ObservableCollection<LocalModFile>();
        public ObservableCollection<string> InstalledMods { get; } = new ObservableCollection<string>();

        public ResourcesViewModel()
        {
            _modrinthService = new ModrinthService();
            _modpackService = new ModpackService();
            _configService = ConfigService.Instance;

            IsFeaturedLoading = true;
            _ = LoadInitialAsync();
        }

        private async Task LoadInitialAsync()
        {
            await Task.Delay(100).ConfigureAwait(false);

            // Try preloaded data first (don't wait, just check)
            if (PreloadService.IsPreloaded && PreloadService.CachedTrendingMods.Count > 0)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    TrendingMods.Clear();
                    foreach (var mod in PreloadService.CachedTrendingMods) TrendingMods.Add(mod);

                    NewestMods.Clear();
                    foreach (var mod in PreloadService.CachedNewestMods) NewestMods.Add(mod);

                    IsFeaturedLoading = false;
                });
                return;
            }

            // Load from API directly
            _featuredLoadCts?.Cancel();
            _featuredLoadCts = new CancellationTokenSource();
            try { await LoadFeaturedContentAsync(_featuredLoadCts.Token); }
            catch (OperationCanceledException) { }
        }

        private async Task LoadFeaturedContentAsync(CancellationToken ct = default)
        {
            IsFeaturedLoading = true;
            try
            {
                ct.ThrowIfCancellationRequested();

                var trendingTask = _modrinthService.GetTrendingAsync(6, SelectedCategory, SelectedGameVersion);
                var newestTask = _modrinthService.GetNewestAsync(6, SelectedCategory, SelectedGameVersion);

                await Task.WhenAll(trendingTask, newestTask).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();

                var trending = trendingTask.Result;
                var newest = newestTask.Result;

                if (trending.Count == 0 && newest.Count == 0)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => IsFeaturedLoading = false);
                    return;
                }

                var allProjects = trending.Concat(newest).ToList();

                var imageTask = PreloadImagesAsync(allProjects, ct);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    TrendingMods.Clear();
                    foreach (var mod in trending) TrendingMods.Add(mod);

                    NewestMods.Clear();
                    foreach (var mod in newest) NewestMods.Add(mod);

                    IsFeaturedLoading = false;
                });

                await imageTask;
            }
            catch (OperationCanceledException) { }
            catch
            {
                await Application.Current.Dispatcher.InvokeAsync(() => IsFeaturedLoading = false);
            }
        }

        private async Task PreloadImagesAsync(List<ModProject> projects, CancellationToken ct)
        {
            try
            {
                var tasks = projects.Select(async mod =>
                {
                    if (ct.IsCancellationRequested) return;
                    if (!string.IsNullOrEmpty(mod.IconUrl))
                    {
                        var img = await ImageCache.GetOrLoadAsync(mod.IconUrl, 180).ConfigureAwait(false);
                        if (img != null && !ct.IsCancellationRequested)
                        {
                            await Application.Current.Dispatcher.InvokeAsync(() => mod.IconImage = img);
                        }
                    }
                });
                await Task.WhenAll(tasks);
            }
            catch { }
        }

        [RelayCommand]
        private void SwitchCategory(string category)
        {
            SelectedCategory = category;
            SearchQuery = string.Empty;
            HasSearchResults = false;
            _featuredLoadCts?.Cancel();
            _ = LoadFeaturedContentAsync();
        }

        [RelayCommand]
        private void FilterByVersion(string? version)
        {
            SelectedGameVersion = SelectedGameVersion == version ? null : version;
            SearchQuery = string.Empty;
            HasSearchResults = false;
            _featuredLoadCts?.Cancel();
            _ = LoadFeaturedContentAsync();
        }

        [RelayCommand]
        private async Task Search()
        {
            _searchDebounceCts?.Cancel();
            _searchDebounceCts = new CancellationTokenSource();
            var ct = _searchDebounceCts.Token;

            try
            {
                await Task.Delay(400, ct);
            }
            catch (OperationCanceledException) { return; }

            await ExecuteSearchAsync(ct);
        }

        private async Task ExecuteSearchAsync(CancellationToken ct)
        {
            IsFullPageView = true;
            var mainVM = Application.Current.MainWindow.DataContext as MainViewModel;
            if (mainVM != null) mainVM.IsGlobalResourcesOverlayActive = true;

            IsBusy = true;
            HasSearchResults = false;
            IsSearchEmpty = false;
            SearchResults.Clear();

            try
            {
                var query = SearchQuery ?? "";
                var results = await _modrinthService.SearchProjectsAsync(query, 20, "relevance", SelectedCategory, 0, SelectedGameVersion).ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();

                if (results.Count == 0)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() => IsSearchEmpty = true);
                    return;
                }

                await PreloadImagesAsync(results, ct);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var item in results)
                        SearchResults.Add(item);
                    HasSearchResults = true;
                });
            }
            catch (OperationCanceledException) { }
            catch (System.Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsSearchEmpty = true;
                    iOS26Dialog.Show($"搜索失败: {ex.Message}", "错误", DialogIcon.Error);
                });
            }
            finally
            {
                await Application.Current.Dispatcher.InvokeAsync(() => IsBusy = false);
            }
        }

        [RelayCommand]
        private void ViewMore()
        {
            _searchDebounceCts?.Cancel();
            _searchDebounceCts = new CancellationTokenSource();

            SearchQuery = "";
            _ = ExecuteSearchAsync(_searchDebounceCts.Token);
        }

        [RelayCommand]
        private void GoBack()
        {
            IsFullPageView = false;
            var mainVM = Application.Current.MainWindow.DataContext as MainViewModel;
            if (mainVM != null) mainVM.IsGlobalResourcesOverlayActive = false;

            SearchQuery = "";
            SearchResults.Clear();
            HasSearchResults = false;
        }

        [RelayCommand]
        private async Task LoadMore()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                int offset = SearchResults.Count;
                var query = SearchQuery ?? "";
                var results = await _modrinthService.SearchProjectsAsync(query, 20, "relevance", SelectedCategory, offset).ConfigureAwait(false);

                var imageTask = PreloadImagesAsync(results, CancellationToken.None);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var item in results)
                        SearchResults.Add(item);
                });

                await imageTask;
            }
            catch { }
            finally { await Application.Current.Dispatcher.InvokeAsync(() => IsBusy = false); }
        }

        [RelayCommand]
        private void DownloadMod(ModProject project)
        {
            ViewDetail(project);
        }

        private async Task DownloadProjectRecursive(string projectId, string? gameVersion, System.Collections.Generic.HashSet<string> visited)
        {
            if (visited.Contains(projectId)) return;
            visited.Add(projectId);

            var versions = await _modrinthService.GetVersionsAsync(projectId, gameVersion);
            var bestMatch = versions.FirstOrDefault();

            if (bestMatch == null) return;

            var mainVM = ((App)Application.Current).MainWindow.DataContext as MainViewModel;
            string gamePath = mainVM?.ConfigService.Settings.GamePath ?? ".minecraft";
            string modsDir = System.IO.Path.Combine(gamePath, "mods");
            System.IO.Directory.CreateDirectory(modsDir);
            string dest = System.IO.Path.Combine(modsDir, bestMatch.FileName);

            if (!System.IO.File.Exists(dest))
            {
                var downloadService = new GeminiLauncher.Services.Network.DownloadService();
                await downloadService.DownloadFileAsync(bestMatch.DownloadUrl, dest, null);
            }

            if (bestMatch.Dependencies != null)
            {
                foreach (var dep in bestMatch.Dependencies)
                {
                    if (dep.DependencyType == "required" && !string.IsNullOrEmpty(dep.ProjectId))
                    {
                        await DownloadProjectRecursive(dep.ProjectId, gameVersion, visited);
                    }
                }
            }
        }

        [RelayCommand]
        private async Task ImportModpack()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Modrinth Modpack (*.mrpack)|*.mrpack",
                Title = "Import Modpack"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    IsImporting = true;
                    ImportProgress = 0;
                    ImportStatus = "Initializing...";

                    var progress = new System.Progress<double>(p => ImportProgress = p);
                    var status = new System.Progress<string>(s => ImportStatus = s);

                    await _modpackService.ImportMrPackAsync(dialog.FileName,
                        string.IsNullOrWhiteSpace(_configService.Settings.GamePath) ? ".minecraft" : _configService.Settings.GamePath,
                        progress, status);

                    iOS26Dialog.Show("整合包导入成功！", "成功", DialogIcon.Success);
                }
                catch (System.Exception ex)
                {
                    iOS26Dialog.Show($"导入失败: {ex.Message}", "错误", DialogIcon.Error);
                }
                finally
                {
                    IsImporting = false;
                }
            }
        }

        [RelayCommand]
        private void ToggleLocalModPanel()
        {
            IsLocalModPanelOpen = !IsLocalModPanelOpen;
            if (IsLocalModPanelOpen)
            {
                LoadInstalledMods();
            }
        }

        [RelayCommand]
        private void AddLocalMod()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Mod文件 (*.jar;*.zip)|*.jar;*.zip|所有文件 (*.*)|*.*",
                Title = "选择Mod或材质包文件",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                foreach (var filePath in dialog.FileNames)
                {
                    if (LocalMods.Any(m => m.FilePath == filePath)) continue;

                    var fileInfo = new FileInfo(filePath);
                    var extension = fileInfo.Extension.ToLowerInvariant();
                    var fileType = extension switch
                    {
                        ".jar" => "Mod",
                        ".zip" => "材质包",
                        _ => "未知"
                    };

                    var localMod = new LocalModFile
                    {
                        FileName = fileInfo.Name,
                        FilePath = filePath,
                        FileType = fileType,
                        FileSize = fileInfo.Length,
                        IsEnabled = true
                    };

                    LocalMods.Add(localMod);
                }
            }
        }

        [RelayCommand]
        private void RemoveLocalMod(LocalModFile? mod)
        {
            if (mod != null && LocalMods.Contains(mod))
            {
                LocalMods.Remove(mod);
            }
        }

        [RelayCommand]
        private void ApplyLocalMods()
        {
            if (LocalMods.Count == 0)
            {
                iOS26Dialog.Show("请先添加Mod或材质包文件", "提示", DialogIcon.Info);
                return;
            }

            var mainVM = Application.Current.MainWindow.DataContext as MainViewModel;
            string gamePath = mainVM?.ConfigService.Settings.GamePath ?? ".minecraft";

            if (string.IsNullOrEmpty(mainVM?.SelectedVersion?.Id))
            {
                iOS26Dialog.Show("请先选择一个游戏版本", "提示", DialogIcon.Warning);
                return;
            }

            string versionId = mainVM.SelectedVersion.Id;
            if (versionId.Contains(" "))
                versionId = versionId.Split(' ')[0];

            // Use the version's working directory so isolated versions get their
            // mods in the right place instead of the global .minecraft folder.
            string targetDir = mainVM?.SelectedVersion?.GameDir;
            if (string.IsNullOrEmpty(targetDir)) targetDir = gamePath;

            var modsDir = Path.Combine(targetDir, "mods");
            var resourcePacksDir = Path.Combine(targetDir, "resourcepacks");

            Directory.CreateDirectory(modsDir);
            Directory.CreateDirectory(resourcePacksDir);

            int successCount = 0;
            foreach (var mod in LocalMods.Where(m => m.IsEnabled))
            {
                try
                {
                    string destDir = mod.FileType == "材质包" ? resourcePacksDir : modsDir;
                    string destPath = Path.Combine(destDir, mod.FileName);

                    if (!File.Exists(destPath) || iOS26Dialog.Show($"文件 {mod.FileName} 已存在，是否覆盖？", "确认", DialogIcon.Warning, DialogButtons.YesNo) == true)
                    {
                        File.Copy(mod.FilePath, destPath, true);
                        successCount++;

                        if (!InstalledMods.Contains(mod.FileName))
                            InstalledMods.Add(mod.FileName);
                    }
                }
                catch (Exception ex)
                {
                    iOS26Dialog.Show($"复制文件失败: {ex.Message}", "错误", DialogIcon.Error);
                }
            }

            if (successCount > 0)
            {
                iOS26Dialog.Show($"成功安装 {successCount} 个文件到版本 {versionId}", "成功", DialogIcon.Success);
                IsLocalModPanelOpen = false;
            }
        }

        [RelayCommand]
        private void PreviewLocalMod(LocalModFile? mod)
        {
            if (mod == null) return;
            SelectedLocalMod = mod;

            if (mod.FileType == "材质包" && Path.GetExtension(mod.FilePath).ToLowerInvariant() == ".zip")
            {
                try
                {
                    using var archive = System.IO.Compression.ZipFile.OpenRead(mod.FilePath);
                    var iconEntry = archive.Entries.FirstOrDefault(e =>
                        e.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                        e.Name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                        e.Name == "pack.png" || e.Name == "pack.jpg");

                    if (iconEntry != null)
                    {
                        using var stream = iconEntry.Open();
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        bitmap.BeginInit();
                        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.StreamSource = stream;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        mod.PreviewImage = bitmap;
                    }
                }
                catch { }
            }
        }

        private void LoadInstalledMods()
        {
            InstalledMods.Clear();
            var mainVM = Application.Current.MainWindow.DataContext as MainViewModel;
            string gamePath = mainVM?.ConfigService.Settings.GamePath ?? ".minecraft";
            string targetDir = mainVM?.SelectedVersion?.GameDir;
            if (string.IsNullOrEmpty(targetDir)) targetDir = gamePath;
            string modsDir = Path.Combine(targetDir, "mods");

            if (Directory.Exists(modsDir))
            {
                foreach (var file in Directory.GetFiles(modsDir, "*.*")
                    .Where(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                {
                    InstalledMods.Add(Path.GetFileName(file));
                }
            }
        }

        [RelayCommand]
        private void RemoveInstalledMod(string? fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;

            if (iOS26Dialog.Show($"确定要删除已安装的 {fileName} 吗？", "确认删除", DialogIcon.Warning, DialogButtons.YesNo) != true)
                return;

            var mainVM = Application.Current.MainWindow.DataContext as MainViewModel;
            string gamePath = mainVM?.ConfigService.Settings.GamePath ?? ".minecraft";
            string targetDir = mainVM?.SelectedVersion?.GameDir;
            if (string.IsNullOrEmpty(targetDir)) targetDir = gamePath;
            string modsDir = Path.Combine(targetDir, "mods");
            string filePath = Path.Combine(modsDir, fileName);

            try
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                    InstalledMods.Remove(fileName);
                    iOS26Dialog.Show("删除成功", "成功", DialogIcon.Success);
                }
            }
            catch (Exception ex)
            {
                iOS26Dialog.Show($"删除失败: {ex.Message}", "错误", DialogIcon.Error);
            }
        }

        [RelayCommand]
        private void ViewDetail(ModProject? project)
        {
            if (project == null) return;

            var mainVM = Application.Current.MainWindow.DataContext as MainViewModel;
            string? gameVersion = mainVM?.SelectedVersion?.Id;
            if (!string.IsNullOrEmpty(gameVersion) && gameVersion.Contains(" "))
                gameVersion = gameVersion.Split(' ')[0];

            _ = PreloadDetailAsync(project.Id);

            var detailPage = new Views.ResourceDetailPage(project, gameVersion);
            NavigationService?.Navigate(detailPage);
        }

        public static async Task PreloadDetailAsync(string projectId)
        {
            if (_preloadCache.ContainsKey(projectId)) return;

            var service = new ModrinthService();
            _preloadCache.TryAdd(projectId, service.GetProjectDetailAsync(projectId));
        }

        public static ResourceDetail? GetPreloadedDetail(string projectId)
        {
            if (_preloadCache.TryGetValue(projectId, out var task) && task.IsCompletedSuccessfully)
                return task.Result;
            return null;
        }

        public static async Task<ResourceDetail?> WaitForPreloadedDetailAsync(string projectId)
        {
            if (_preloadCache.TryGetValue(projectId, out var task))
            {
                try
                {
                    return await task;
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        private System.Windows.Navigation.NavigationService? NavigationService
        {
            get
            {
                foreach (var page in Application.Current.Windows.OfType<Window>())
                {
                    if (page is MainWindow mw)
                    {
                        var rootFrame = mw.FindName("RootFrame") as Frame;
                        if (rootFrame != null)
                            return rootFrame.NavigationService;
                    }
                }
                return null;
            }
        }
    }
}
