using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TsuruLauncher.Models;
using TsuruLauncher.Services;
using TsuruLauncher.Services.Ecosystem;
using TsuruLauncher.Services.Network;

namespace TsuruLauncher.ViewModels
{
    public partial class LoaderSelectionViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _mcVersion = string.Empty;

        [ObservableProperty]
        private DownloadableVersion? _selectedVersion;

        [ObservableProperty]
        private bool _isForgeLoading;

        [ObservableProperty]
        private bool _isFabricLoading;

        [ObservableProperty]
        private bool _isOptifineLoading;

        [ObservableProperty]
        private bool _forgeExpanded;

        [ObservableProperty]
        private bool _fabricExpanded;

        [ObservableProperty]
        private bool _optifineExpanded;

        [ObservableProperty]
        private string _forgeSelectedVersion = string.Empty;

        [ObservableProperty]
        private string _fabricSelectedVersion = string.Empty;

        [ObservableProperty]
        private string _optifineSelectedVersion = string.Empty;

        [ObservableProperty]
        private LoaderVersionItem? _selectedForgeItem;

        [ObservableProperty]
        private LoaderVersionItem? _selectedFabricItem;

        [ObservableProperty]
        private LoaderVersionItem? _selectedOptifineItem;

        [ObservableProperty]
        private bool _isDownloading;

        [ObservableProperty]
        private string _downloadStatus = string.Empty;

        [ObservableProperty]
        private double _downloadProgress;

        [ObservableProperty]
        private string _forgeFilterText = string.Empty;

        [ObservableProperty]
        private string _fabricFilterText = string.Empty;

        [ObservableProperty]
        private string _optifineFilterText = string.Empty;

        public ObservableCollection<LoaderVersionItem> ForgeVersions { get; } = new();
        public ObservableCollection<LoaderVersionItem> FabricVersions { get; } = new();
        public ObservableCollection<LoaderVersionItem> OptiFineVersions { get; } = new();

        public ObservableCollection<LoaderVersionItem> FilteredForgeVersions { get; } = new();
        public ObservableCollection<LoaderVersionItem> FilteredFabricVersions { get; } = new();
        public ObservableCollection<LoaderVersionItem> FilteredOptiFineVersions { get; } = new();

        private readonly LoaderApiService _loaderApiService = new();
        private bool _forgeLoaded, _fabricLoaded, _optifineLoaded;

        private DownloadTask? _currentDownloadTask;

        public LoaderSelectionViewModel()
        {
        }

        public void Initialize(DownloadableVersion version)
        {
            SelectedVersion = version;
            McVersion = version.Id;
        }

        partial void OnSelectedForgeItemChanged(LoaderVersionItem? value)
        {
            ForgeSelectedVersion = value?.Version ?? "";
        }

        partial void OnSelectedFabricItemChanged(LoaderVersionItem? value)
        {
            FabricSelectedVersion = value?.Version ?? "";
        }

        partial void OnSelectedOptifineItemChanged(LoaderVersionItem? value)
        {
            OptifineSelectedVersion = value?.Version ?? "";
        }

        partial void OnForgeFilterTextChanged(string value)
        {
            ApplyFilter(ForgeVersions, FilteredForgeVersions, value);
        }

        partial void OnFabricFilterTextChanged(string value)
        {
            ApplyFilter(FabricVersions, FilteredFabricVersions, value);
        }

        partial void OnOptifineFilterTextChanged(string value)
        {
            ApplyFilter(OptiFineVersions, FilteredOptiFineVersions, value);
        }

        private void ApplyFilter(ObservableCollection<LoaderVersionItem> source, ObservableCollection<LoaderVersionItem> target, string filter)
        {
            target.Clear();
            IEnumerable<LoaderVersionItem> filtered;
            if (string.IsNullOrWhiteSpace(filter))
                filtered = source;
            else
                filtered = source.Where(v => v.Version.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

            foreach (var item in filtered) target.Add(item);
        }

        [RelayCommand]
        private void ToggleForge()
        {
            ForgeExpanded = !ForgeExpanded;
            if (ForgeExpanded && !_forgeLoaded)
            {
                _forgeLoaded = true;
                IsForgeLoading = true;
                Task.Run(() =>
                {
                    try { return _loaderApiService.GetForgeVersions(McVersion); }
                    catch { return new System.Collections.Generic.List<LoaderVersionItem>(); }
                }).ContinueWith(t =>
                {
                    if (!Application.Current.Dispatcher.HasShutdownStarted)
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ForgeVersions.Clear();
                            FilteredForgeVersions.Clear();
                            foreach (var v in t.Result)
                            {
                                ForgeVersions.Add(v);
                                FilteredForgeVersions.Add(v);
                            }
                            if (ForgeVersions.Count > 0) SelectedForgeItem = ForgeVersions[0];
                            IsForgeLoading = false;
                        });
                });
            }
        }

        [RelayCommand]
        private void ToggleFabric()
        {
            FabricExpanded = !FabricExpanded;
            if (FabricExpanded && !_fabricLoaded)
            {
                _fabricLoaded = true;
                IsFabricLoading = true;
                Task.Run(() =>
                {
                    try { return _loaderApiService.GetFabricVersions(McVersion); }
                    catch { return new System.Collections.Generic.List<LoaderVersionItem>(); }
                }).ContinueWith(t =>
                {
                    if (!Application.Current.Dispatcher.HasShutdownStarted)
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            FabricVersions.Clear();
                            FilteredFabricVersions.Clear();
                            foreach (var v in t.Result)
                            {
                                FabricVersions.Add(v);
                                FilteredFabricVersions.Add(v);
                            }
                            if (FabricVersions.Count > 0) SelectedFabricItem = FabricVersions[0];
                            IsFabricLoading = false;
                        });
                });
            }
        }

        [RelayCommand]
        private void ToggleOptifine()
        {
            OptifineExpanded = !OptifineExpanded;
            if (OptifineExpanded && !_optifineLoaded)
            {
                _optifineLoaded = true;
                IsOptifineLoading = true;
                Task.Run(() =>
                {
                    try { return _loaderApiService.GetOptiFineVersions(McVersion); }
                    catch { return new System.Collections.Generic.List<LoaderVersionItem>(); }
                }).ContinueWith(t =>
                {
                    if (!Application.Current.Dispatcher.HasShutdownStarted)
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            OptiFineVersions.Clear();
                            FilteredOptiFineVersions.Clear();
                            foreach (var v in t.Result)
                            {
                                OptiFineVersions.Add(v);
                                FilteredOptiFineVersions.Add(v);
                            }
                            if (OptiFineVersions.Count > 0) SelectedOptifineItem = OptiFineVersions[0];
                            IsOptifineLoading = false;
                        });
                });
            }
        }

        [RelayCommand]
        private async Task StartDownload()
        {
            if (IsDownloading || SelectedVersion == null) return;
            IsDownloading = true;
            DownloadStatus = "正在准备下载...";
            DownloadProgress = 0;

            try
            {
                string loaderChoice = "Vanilla";
                string loaderVersion = "";

                if (!string.IsNullOrEmpty(ForgeSelectedVersion))
                {
                    loaderChoice = "Forge";
                    loaderVersion = ForgeSelectedVersion;
                }
                else if (!string.IsNullOrEmpty(FabricSelectedVersion))
                {
                    loaderChoice = "Fabric";
                    loaderVersion = FabricSelectedVersion;
                }
                else if (!string.IsNullOrEmpty(OptifineSelectedVersion))
                {
                    loaderChoice = "OptiFine";
                    loaderVersion = OptifineSelectedVersion;
                }

                var downloadSource = ConfigService.Instance.Settings.DownloadSource;

                // Single task shared with the download manager, so the panel shows one
                // entry and Cancel/Pause actually cancel the running download.
                _currentDownloadTask = new DownloadTask
                {
                    Name = loaderChoice == "Vanilla" ? SelectedVersion.Id : $"{SelectedVersion.Id}-{loaderChoice}",
                    Status = "正在下载游戏核心...",
                    Cts = new CancellationTokenSource(),
                    VersionId = SelectedVersion.Id,
                    LoaderChoice = loaderChoice,
                    LoaderVersion = loaderVersion,
                    Source = downloadSource
                };
                DownloadManagerService.Instance.EnqueueTask(_currentDownloadTask);

                await DownloadManagerService.Instance.EnqueueGameDownloadWithLoader(
                    _currentDownloadTask, SelectedVersion, loaderChoice, loaderVersion, downloadSource);

                if (_currentDownloadTask.IsCompleted)
                {
                    DownloadStatus = "下载并安装完成！";
                    DownloadProgress = 1.0;

                    // Refresh the version list and open the mod manager for the
                    // freshly installed loader version — like PCL2, game + loader
                    // are one unit and the mod screen is one tap away.
                    if (loaderChoice != "Vanilla")
                        await OpenModManagerForInstalledVersionAsync(loaderChoice);
                }
                else
                {
                    DownloadStatus = _currentDownloadTask.ErrorMessage ?? "下载未完成";
                }
            }
            catch (OperationCanceledException)
            {
                DownloadStatus = "已取消下载";
                if (_currentDownloadTask != null)
                {
                    _currentDownloadTask.Status = "已取消";
                    _currentDownloadTask.IsFailed = true;
                }
            }
            catch (Exception ex)
            {
                DownloadStatus = $"下载失败: {ex.Message}";
                if (_currentDownloadTask != null)
                {
                    _currentDownloadTask.Status = "Failed";
                    _currentDownloadTask.IsFailed = true;
                    _currentDownloadTask.ErrorMessage = ex.Message;
                }
            }
            finally
            {
                IsDownloading = false;
            }
        }

        /// <summary>
        /// After a game+loader install finishes, refresh the version list and
        /// navigate to that version's mod manager.
        /// </summary>
        private async Task OpenModManagerForInstalledVersionAsync(string loaderChoice)
        {
            try
            {
                var mainVM = Application.Current.MainWindow.DataContext as MainViewModel;
                if (mainVM == null) return;

                await mainVM.LoadVersionsAsync();

                string mcVersion = SelectedVersion?.Id ?? "";
                var installed = mainVM.GameVersions.FirstOrDefault(v =>
                        v.Id == _currentDownloadTask?.VersionId &&
                        v.Id.Contains(loaderChoice, StringComparison.OrdinalIgnoreCase))
                    ?? mainVM.GameVersions.FirstOrDefault(v =>
                        v.Id.StartsWith(mcVersion, StringComparison.OrdinalIgnoreCase) &&
                        v.Id.Contains(loaderChoice, StringComparison.OrdinalIgnoreCase))
                    ?? mainVM.GameVersions.FirstOrDefault(v => v.Id == _currentDownloadTask?.VersionId);

                if (installed != null && Application.Current.MainWindow is MainWindow mw)
                {
                    mw.RootFrame.Navigate(new Views.VersionSettingsPage(installed, "mods"));
                }
            }
            catch { }
        }

        [RelayCommand]
        private void PauseDownload()
        {
            if (_currentDownloadTask != null && !_currentDownloadTask.IsCompleted && !_currentDownloadTask.IsFailed)
            {
                _currentDownloadTask.Cts.Cancel();
                _currentDownloadTask.Status = "已暂停";
                DownloadStatus = "下载已暂停";
            }
        }

        [RelayCommand]
        private void CancelDownload()
        {
            if (_currentDownloadTask != null && !_currentDownloadTask.IsCompleted && !_currentDownloadTask.IsFailed)
            {
                _currentDownloadTask.Cts.Cancel();
                _currentDownloadTask.Status = "已取消";
                _currentDownloadTask.IsFailed = true;
                DownloadStatus = "下载已取消";
                IsDownloading = false;
            }
        }

        [RelayCommand]
        private void GoBack()
        {
            if (Application.Current.MainWindow is MainWindow mw)
                mw.RootFrame.GoBack();
        }
    }

    public class LoaderVersionItem
    {
        public string Version { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public bool IsRecommended { get; set; }
        public bool IsLatest { get; set; }
    }
}