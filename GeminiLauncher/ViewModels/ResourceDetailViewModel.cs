using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeminiLauncher.Models.Ecosystem;
using GeminiLauncher.Services.Ecosystem;
using GeminiLauncher.Services.Network;
using GeminiLauncher.Services;
using GeminiLauncher.Views;

namespace GeminiLauncher.ViewModels
{
    public partial class ResourceDetailViewModel : ObservableObject
    {
        private readonly ModrinthService _modrinthService;
        private readonly DownloadManagerService _downloadManager;

        [ObservableProperty]
        private ResourceDetail? _resource;

        [ObservableProperty]
        private ModProject? _sourceProject;

        [ObservableProperty]
        private string? _targetGameVersion;

        [ObservableProperty]
        private string _selectedLoaderFilter = "All";

        [ObservableProperty]
        private string _selectedVersionSort = "Newest";

        [ObservableProperty]
        private List<ModFile> _filteredVersions = new();

        [ObservableProperty]
        private bool _isVersionsLoading;

        [ObservableProperty]
        private int _selectedGalleryIndex;

        [ObservableProperty]
        private bool _showDependenciesPanel;

        [ObservableProperty]
        private List<ModDependency> _dependencyList = new();

        [ObservableProperty]
        private bool _downloadDependencies = true;

        [ObservableProperty]
        private string _installPathDisplay = "";

        [ObservableProperty]
        private ModFile? _selectedVersionItem;

        [ObservableProperty]
        private bool _isFailed;

        [ObservableProperty]
        private bool _isLoadingDetails;

        [ObservableProperty]
        private bool _showDownloadProgressButton;

        public List<string> LoaderOptions { get; } = new() { "All", "Fabric", "Forge", "Quilt", "NeoForge" };
        public List<string> VersionSortOptions { get; } = new() { "Newest", "Oldest" };
        public List<DownloadSource> SourceOptions { get; } = Enum.GetValues(typeof(DownloadSource)).Cast<DownloadSource>().ToList();

        public ResourceDetailViewModel()
        {
            _modrinthService = new ModrinthService();
            _downloadManager = DownloadManagerService.Instance;
        }

        public async Task InitializeAsync(ModProject project, string? gameVersion = null)
        {
            SourceProject = project;
            TargetGameVersion = gameVersion;
            
            Resource = new ResourceDetail
            {
                Id = project.Id,
                Name = project.Name,
                Summary = project.Summary,
                IconUrl = project.IconUrl,
                IconImage = project.IconImage,
                Author = project.Author,
                Downloads = project.Downloads,
                Platform = project.Platform,
                Type = project.Type,
                WebUrl = project.WebUrl
            };

            IsLoadingDetails = true;
            try
            {
                // Try to use preloaded detail from cache first
                var preloadedDetail = ResourcesViewModel.GetPreloadedDetail(project.Id);
                
                // Load detail and versions in parallel
                var detailTask = preloadedDetail != null 
                    ? Task.FromResult<ResourceDetail?>(preloadedDetail)
                    : _modrinthService.GetProjectDetailAsync(project.Id);
                var versionsTask = _modrinthService.GetVersionsAsync(project.Id, gameVersion);

                await Task.WhenAll(detailTask, versionsTask);

                var detail = await detailTask;
                var versions = await versionsTask;

                if (versions.Count == 0 && !string.IsNullOrEmpty(gameVersion))
                    versions = await _modrinthService.GetVersionsAsync(project.Id);
                
                if (detail != null)
                {
                    Resource = detail;

                    if (versions != null)
                    {
                        Resource.Versions = versions;
                        FilteredVersions = versions.OrderByDescending(v => v.ReleaseDate).Take(50).ToList();
                        UpdateInstallPath();
                        if (FilteredVersions.Count > 0)
                            SelectBestMatchVersion();
                    }
                }
                else
                {
                    Resource.DownloadStatus = "加载失败：无法获取项目详情";
                    IsFailed = true;
                }
            }
            catch (Exception ex)
            {
                Resource.DownloadStatus = $"加载异常: {ex.Message}";
                IsFailed = true;
            }
            finally
            {
                IsLoadingDetails = false;
            }
        }

        partial void OnSelectedLoaderFilterChanged(string value) => ApplyFilters();
        partial void OnSelectedVersionSortChanged(string value) => ApplyFilters();

        private void ApplyFilters()
        {
            if (Resource == null) return;

            var versions = Resource.Versions.AsEnumerable();

            if (!string.IsNullOrEmpty(TargetGameVersion))
            {
                versions = versions.Where(v =>
                    v.GameVersions.Contains(TargetGameVersion, StringComparer.OrdinalIgnoreCase) ||
                    v.GameVersions.Count == 0);
            }

            if (SelectedLoaderFilter != "All")
            {
                versions = versions.Where(v =>
                    v.Loaders.Contains(SelectedLoaderFilter, StringComparer.OrdinalIgnoreCase) ||
                    v.Loaders.Count == 0);
            }

            versions = SelectedVersionSort switch
            {
                "Newest" => versions.OrderByDescending(v => v.ReleaseDate),
                "Oldest" => versions.OrderBy(v => v.ReleaseDate),
                _ => versions.OrderByDescending(v => v.ReleaseDate)
            };

            FilteredVersions = versions.Take(50).ToList();

            if (FilteredVersions.Count > 0 && Resource.SelectedVersion == null)
            {
                SelectBestMatchVersion();
            }
        }

        private void SelectBestMatchVersion()
        {
            if (Resource == null || FilteredVersions.Count == 0) return;

            var best = FilteredVersions.FirstOrDefault();
            if (!string.IsNullOrEmpty(TargetGameVersion))
            {
                best = FilteredVersions.FirstOrDefault(v =>
                    v.GameVersions.Contains(TargetGameVersion)) ?? best;
            }

            foreach (var v in FilteredVersions)
                v.IsSelected = false;
            if (best != null) best.IsSelected = true;

            Resource.SelectedVersion = best;
            SelectedVersionItem = best;
            ShowDependencyInfo(best);
        }

        [RelayCommand]
        private void SelectVersion(ModFile? version)
        {
            if (version == null || Resource == null) return;

            foreach (var v in FilteredVersions)
                v.IsSelected = false;

            version.IsSelected = true;
            Resource.SelectedVersion = version;
            SelectedVersionItem = version;
            ShowDependencyInfo(version);
        }

        partial void OnSelectedVersionItemChanged(ModFile? value)
        {
            if (value == null || Resource == null) return;

            foreach (var v in FilteredVersions)
                v.IsSelected = false;
            value.IsSelected = true;
            Resource.SelectedVersion = value;
            ShowDependencyInfo(value);
        }

        private void ShowDependencyInfo(ModFile version)
        {
            if (version.Dependencies != null && version.Dependencies.Count > 0)
            {
                DependencyList = version.Dependencies.ToList();
                ShowDependenciesPanel = true;
            }
            else
            {
                DependencyList.Clear();
                ShowDependenciesPanel = false;
            }
        }

        [RelayCommand]
        private void StartDownload()
        {
            if (Resource?.SelectedVersion == null || Resource.IsDownloading)
            {
                if (Resource != null) Resource.DownloadStatus = "请先选择一个版本";
                return;
            }

            if (string.IsNullOrWhiteSpace(Resource.SelectedVersion.DownloadUrl))
            {
                Resource.DownloadStatus = "下载链接无效";
                return;
            }

            var mainVM = ((App)Application.Current).MainWindow.DataContext as MainViewModel;
            string gamePath = mainVM?.ConfigService.Settings.GamePath ?? ".minecraft";
            // Use the selected version's working directory (respects version isolation)
            string targetDir = mainVM?.SelectedVersion?.GameDir;
            if (string.IsNullOrEmpty(targetDir)) targetDir = gamePath;
            string destDir = GetDestinationDirectory(targetDir);
            Directory.CreateDirectory(destDir);
            string destPath = Path.Combine(destDir, Resource.SelectedVersion.FileName);

            // Show notification
            mainVM?.NotificationService.ShowSuccess("下载任务", $"已加入下载任务：{Resource.Name}");

            // Show download progress button, hide download button
            ShowDownloadProgressButton = true;
            Resource.IsDownloading = true;
            Resource.DownloadStatus = "已加入下载队列";

            // Enqueue to download manager (fire and forget, don't await on UI)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _downloadManager.EnqueueGenericDownload(
                        Resource.Name,
                        Resource.SelectedVersion!.DownloadUrl,
                        destPath);

                    if (DownloadDependencies && Resource.SelectedVersion.Dependencies?.Count > 0)
                    {
                        await DownloadDependenciesAsync(targetDir);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[ResourceDetail] Download error: {ex.Message}");
                }
            });
        }

        [RelayCommand]
        private void NavigateToDownloadManager()
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.RootFrame.Navigate(new DownloadManagerPage());
            }
        }

        private async Task DownloadDependenciesAsync(string targetDir)
        {
            if (Resource?.SelectedVersion?.Dependencies == null) return;

            foreach (var dep in Resource.SelectedVersion.Dependencies.Where(d => d.DependencyType == "required"))
            {
                try
                {
                    var depVersions = await _modrinthService.GetVersionsAsync(dep.ProjectId, TargetGameVersion);
                    var depFile = depVersions.FirstOrDefault();

                    if (depFile != null)
                    {
                        string modsDir = Path.Combine(targetDir, "mods");
                        Directory.CreateDirectory(modsDir);
                        string depDest = Path.Combine(modsDir, depFile.FileName);

                        if (!File.Exists(depDest))
                        {
                            await _downloadManager.EnqueueGenericDownload(
                                dep.FileName ?? dep.ProjectId,
                                depFile.DownloadUrl,
                                depDest);
                        }
                    }
                }
                catch { }
            }
        }

        [RelayCommand]
        private void OpenInBrowser()
        {
            if (Resource?.WebUrl != null)
            {
                try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Resource.WebUrl) { UseShellExecute = true }); }
                catch { }
            }
        }

        [RelayCommand]
        private void CopyDownloadLink()
        {
            if (Resource?.SelectedVersion?.DownloadUrl != null)
            {
                try { Clipboard.SetText(Resource.SelectedVersion.DownloadUrl); }
                catch { }
            }
        }

        private string GetDestinationDirectory(string gamePath)
        {
            return Resource?.Type switch
            {
                ProjectType.ResourcePack => Path.Combine(gamePath, "resourcepacks"),
                ProjectType.Shader => Path.Combine(gamePath, "shaderpacks"),
                ProjectType.DataPack => Path.Combine(gamePath, "datapacks"),
                _ => Path.Combine(gamePath, "mods")
            };
        }

        private void UpdateInstallPath()
        {
            if (Resource == null) return;
            var mainVM = ((App)Application.Current).MainWindow.DataContext as MainViewModel;
            var gamePath = mainVM?.ConfigService.Settings.GamePath ?? ".minecraft";
            string targetDir = mainVM?.SelectedVersion?.GameDir;
            if (string.IsNullOrEmpty(targetDir)) targetDir = gamePath;
            string dir = GetDestinationDirectory(targetDir);
            InstallPathDisplay = Path.Combine(dir, Resource.SelectedVersion?.FileName ?? "{未选择版本}");
        }

        partial void OnTargetGameVersionChanged(string? value)
        {
            ApplyFilters();
            UpdateInstallPath();
        }

        partial void OnResourceChanged(ResourceDetail? value)
        {
            GeminiLauncher.Converters.DataContextHelper.CurrentResource = value;
        }
    }
}
