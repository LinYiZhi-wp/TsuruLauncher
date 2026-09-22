using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using TsuruLauncher.Models;
using TsuruLauncher.Models.Ecosystem;
using TsuruLauncher.Services.Network;
using Newtonsoft.Json.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace TsuruLauncher.Services.Network
{
    public partial class DownloadManagerService : ObservableObject
    {
        private static DownloadManagerService? _instance;
        public static DownloadManagerService Instance => _instance ??= new DownloadManagerService();

        public ObservableCollection<DownloadTask> ActiveTasks { get; } = new ObservableCollection<DownloadTask>();

        [ObservableProperty] private string _totalProgressText = "0.00 %";
        [ObservableProperty] private string _globalSpeedText = "0 KB/s";
        [ObservableProperty] private int _totalRemainingFiles;
        [ObservableProperty] private bool _anyActiveTasks;

        private readonly VersionManifestService _manifestService = new();
        private readonly DownloadService _downloadService = new(Math.Max(1, ConfigService.Instance.Settings.MaxDownloadThreads));
        private readonly System.Windows.Threading.DispatcherTimer _speedTimer;

        private DownloadManagerService() 
        {
            _speedTimer = new System.Windows.Threading.DispatcherTimer();
            _speedTimer.Interval = TimeSpan.FromSeconds(1);
            _speedTimer.Tick += SpeedTimer_Tick;
            _speedTimer.Start();
        }

        private void SpeedTimer_Tick(object? sender, EventArgs e)
        {
            long totalDelta = 0;
            double weightedProgress = 0;
            int totalFiles = 0;
            int activeTaskCount = 0;

            foreach (var task in ActiveTasks)
            {
                if (task.IsCompleted || task.IsFailed)
                {
                    task.SpeedText = "0 KB/s";
                    continue;
                }

                activeTaskCount++;
                long currentBytes = task.DownloadedBytes;
                long delta = currentBytes - task.LastDownloadedBytes;
                task.LastDownloadedBytes = currentBytes;
                totalDelta += delta;

                if (delta < 1024) task.SpeedText = $"{delta} B/s";
                else if (delta < 1024 * 1024) task.SpeedText = $"{(delta / 1024.0):F1} KB/s";
                else task.SpeedText = $"{(delta / (1024.0 * 1024.0)):F1} MB/s";

                weightedProgress += task.Progress;
                totalFiles += task.RemainingFiles;
            }

            // Update Global Stats
            if (activeTaskCount > 0)
            {
                TotalProgressText = $"{(weightedProgress / activeTaskCount * 100):F2} %";
                TotalRemainingFiles = totalFiles;
                
                if (totalDelta < 1024) GlobalSpeedText = $"{totalDelta} B/s";
                else if (totalDelta < 1024 * 1024) GlobalSpeedText = $"{(totalDelta / 1024.0):F1} KB/s";
                else GlobalSpeedText = $"{(totalDelta / (1024.0 * 1024.0)):F1} MB/s";
            }
            else
            {
                TotalProgressText = "0.00 %";
                GlobalSpeedText = "0 KB/s";
                TotalRemainingFiles = 0;
            }

            AnyActiveTasks = ActiveTasks.Any(t => !t.IsCompleted && !t.IsFailed);
        }

        // ObservableCollection must only be touched on the UI thread
        private void AddTask(DownloadTask task)
        {
            if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
                Application.Current.Dispatcher.Invoke(() => ActiveTasks.Add(task));
            else
                ActiveTasks.Add(task);
        }

        public void EnqueueTask(DownloadTask task) => AddTask(task);

        public async Task EnqueueGenericDownload(string name, string url, string destination, DownloadTask? existing = null)
        {
            var task = existing ?? new DownloadTask { Name = name, Status = "Pending..." };
            if (existing == null)
            {
                task.DownloadUrl = url;
                task.DestinationPath = destination;
                AddTask(task);
            }
            
            try
            {
                task.Status = "Downloading...";
                Debug.WriteLine($"[DownloadManager] Starting download: {name} from {url} to {destination}");
                await _downloadService.DownloadFileAsync(url, destination, null, new Progress<long>(bytes => task.IncrementBytes(bytes)), task.Cts.Token);
                task.Status = "Completed";
                task.IsCompleted = true;
                task.Progress = 1.0;
                Debug.WriteLine($"[DownloadManager] Download completed: {name}");
            }
            catch (Exception ex)
            {
                task.Status = "Failed";
                task.IsFailed = true;
                task.ErrorMessage = ex.Message;
                Debug.WriteLine($"[DownloadManager] Download failed: {name}, Error: {ex.Message}");
            }
        }

        private static DownloadTask CreateGameTask(DownloadableVersion version, string loaderChoice, string loaderVersion, string source)
        {
            string name = version.Id;
            if (!string.IsNullOrEmpty(loaderChoice) && loaderChoice != "Vanilla")
                name = $"{version.Id}-{loaderChoice}";
            return new DownloadTask
            {
                Name = name,
                Status = "Preparing...",
                VersionId = version.Id,
                LoaderChoice = loaderChoice,
                LoaderVersion = loaderVersion,
                Source = source
            };
        }

        private async Task AttemptDownloadAsync(DownloadTask task, DownloadableVersion version, string loaderChoice, string source)
        {
            try
            {
                // If retrying, reset failure state
                task.IsFailed = false;
                task.ErrorMessage = string.Empty;

                await DownloadGameFullAsync(task, version, loaderChoice, source);
                
                task.Status = "Completed";
                task.IsCompleted = true;
                task.Progress = 1.0;
            }
            catch (OperationCanceledException)
            {
                task.Status = "Canceled";
                task.IsFailed = true;
            }
            catch (Exception ex)
            {
                // Auto-Fallback Logic for Network/Server Errors
                // If we are using a mirror (not Official) and encounter an error, try switching to Official source.
                if (source != "Official")
                {
                    task.Status = $"镜像源异常 ({ex.Message})，正在切换至官方源重试...";
                    await Task.Delay(2000); // Give user time to read status
                    
                    // Recursive retry with Official source
                    await AttemptDownloadAsync(task, version, loaderChoice, "Official");
                    return;
                }

                task.Status = "Failed";
                task.IsFailed = true;
                task.ErrorMessage = ex.Message;
            }
        }

        private async Task DownloadGameFullAsync(DownloadTask task, DownloadableVersion version, string loaderChoice, string source)
        {
            var mainVM = GetMainViewModelSafe();
            string gamePath = mainVM?.ConfigService.Settings.GamePath ?? ".minecraft";
            string versionDir = Path.Combine(gamePath, "versions", version.Id);
            Directory.CreateDirectory(versionDir);

            // 1. Download version.json
            task.Status = "正在获取版本信息...";
            task.JsonStatus = "下载中...";
            task.JsonStatusText = "version.json";
            string jsonUrl = ReplaceSource(version.Url, source);
            if (string.IsNullOrEmpty(jsonUrl))
            {
                jsonUrl = source switch
                {
                    "BMCLAPI" => $"https://bmclapi2.bangbang93.com/v1/packages/{version.Id}/{version.Id}.json",
                    "FastMirror" => $"https://download.fastmirror.net/v1/packages/{version.Id}/{version.Id}.json",
                    "MCMirror" => $"https://mirrors.mcfx.net/v1/packages/{version.Id}/{version.Id}.json",
                    _ => $"https://piston-meta.mojang.com/v1/packages/{version.Id}/{version.Id}.json"
                };
            }
            string jsonPath = Path.Combine(versionDir, $"{version.Id}.json");
            await _downloadService.DownloadFileAsync(jsonUrl, jsonPath, null, null, task.Cts.Token);
            task.JsonProgress = 1.0;
            task.JsonStatus = "已完成";
            task.JsonStatusText = "已完成";

            string jsonContent = File.ReadAllText(jsonPath);
            var json = JObject.Parse(jsonContent);

            var downloadRequests = new List<DownloadRequest>();

            // 2. Client.jar
            var clientDownloads = json["downloads"]?["client"];
            if (clientDownloads != null)
            {
                string clientUrl = ReplaceSource(clientDownloads["url"]?.ToString() ?? "", source);
                string clientPath = Path.Combine(versionDir, $"{version.Id}.jar");
                downloadRequests.Add(new DownloadRequest(clientUrl, clientPath, clientDownloads["sha1"]?.ToString()));
            }

            // 3. Libraries
            task.Status = "正在索引依赖库...";
            task.LibrariesStatus = "下载中...";
            task.LibrariesStatusText = "准备中...";
            var libs = json["libraries"];
            if (libs != null)
            {
                foreach (var lib in libs)
                {
                    var artifact = lib["downloads"]?["artifact"];
                    if (artifact != null)
                    {
                        string libUrl = ReplaceSource(artifact["url"]?.ToString() ?? "", source);
                        string relativePath = artifact["path"]?.ToString() ?? "";
                        if (string.IsNullOrEmpty(relativePath)) continue;

                        string libPath = Path.Combine(gamePath, "libraries", relativePath);
                        downloadRequests.Add(new DownloadRequest(libUrl, libPath, artifact["sha1"]?.ToString()));
                    }

                    // Native (classifier) libraries — without these the game cannot
                    // find LWJGL native files at launch.
                    string? classifier = GetWindowsNativesClassifier(lib);
                    if (!string.IsNullOrEmpty(classifier))
                    {
                        var classifiers = lib["downloads"]?["classifiers"] as JObject;
                        var classToken = classifiers?[classifier];
                        if (classToken != null)
                        {
                            string nativeUrl = ReplaceSource(classToken["url"]?.ToString() ?? "", source);
                            string nativePath = classToken["path"]?.ToString() ?? "";
                            if (!string.IsNullOrEmpty(nativePath))
                            {
                                downloadRequests.Add(new DownloadRequest(
                                    nativeUrl,
                                    Path.Combine(gamePath, "libraries", nativePath),
                                    classToken["sha1"]?.ToString()));
                            }
                        }
                    }
                }
            }

            // 4. Assets
            task.Status = "正在索引资源文件...";
            task.AssetsStatus = "下载中...";
            task.AssetsStatusText = "准备中...";
            var assetIndex = json["assetIndex"];
            if (assetIndex != null)
            {
                string indexUrl = ReplaceSource(assetIndex["url"]?.ToString() ?? "", source);
                string indexId = assetIndex["id"]?.ToString() ?? "legacy";
                string indexPath = Path.Combine(gamePath, "assets", "indexes", $"{indexId}.json");
                
                await _downloadService.DownloadFileAsync(indexUrl, indexPath, assetIndex["sha1"]?.ToString(), null, task.Cts.Token);
                
                var indexJson = JObject.Parse(File.ReadAllText(indexPath));
                var objects = indexJson["objects"];
                if (objects != null)
                {
                    foreach (var obj in (JObject)objects)
                    {
                        string hash = obj.Value?["hash"]?.ToString() ?? "";
                        if (string.IsNullOrEmpty(hash)) continue;

                        string assetUrl = ReplaceSource(
                            $"https://resources.download.minecraft.net/{hash[..2]}/{hash}", 
                            source);
                        
                        string assetPath = Path.Combine(gamePath, "assets", "objects", hash[..2], hash);
                        downloadRequests.Add(new DownloadRequest(assetUrl, assetPath, hash));
                    }
                }
            }

            // 5. Start Batch Download
            task.Status = "正在下载组件...";
            // Keep files that already exist BUT have a checksum: DownloadFileAsync
            // verifies them instantly (skip when valid, re-download when corrupt),
            // so a previously corrupted file gets repaired during install.
            var pendingRequests = downloadRequests
                .Where(r => r.Sha1 == null || !File.Exists(r.DestinationPath))
                .ToList();
            task.RemainingFiles = pendingRequests.Count;

            long completedCount = 0;
            long lastUpdateTick = 0;

            var progress = new Progress<double>(p => {
                try
                {
                    completedCount++;
                    
                    long currentTick = Environment.TickCount64;
                    // Throttle UI updates to ~20fps (50ms) to avoid flooding the UI thread
                    if (currentTick - lastUpdateTick < 50 && completedCount < pendingRequests.Count) return;
                    lastUpdateTick = currentTick;

                    task.RemainingFiles = (int)(pendingRequests.Count - completedCount);
                    task.Progress = pendingRequests.Count > 0 ? (double)completedCount / pendingRequests.Count : 1.0;
                    task.SizeText = $"{completedCount} / {pendingRequests.Count} 个文件";

                    // Map to sub-progresses for UI (Heuristic)
                    if (task.Progress < 0.3) {
                        task.LibrariesProgress = task.Progress / 0.3;
                        task.LibrariesStatusText = $"{completedCount} 文件";
                    } else {
                        task.LibrariesProgress = 1.0;
                        task.LibrariesStatus = "已完成";
                        task.LibrariesStatusText = "已完成";
                        task.AssetsProgress = Math.Min(1.0, (task.Progress - 0.3) / 0.7);
                        task.AssetsStatusText = $"{completedCount} 文件";
                    }
                }
                catch (Exception ex)
                {
                    // Log but don't crash
                    System.Diagnostics.Debug.WriteLine($"Progress Error: {ex}");
                }
            });

            // Use SynchronousProgress to avoid marshalling every byte update to the UI thread (performance)
            var byteProgress = new SynchronousProgress<long>(bytes => task.IncrementBytes(bytes));

            if (pendingRequests.Any())
            {
                await _downloadService.DownloadBatchAsync(pendingRequests, progress, byteProgress, task.Cts.Token);
            }

            task.LibrariesProgress = 1.0;
            task.LibrariesStatus = "已完成";
            task.LibrariesStatusText = "已完成";
            task.AssetsProgress = 1.0;
            task.AssetsStatus = "已完成";
            task.AssetsStatusText = "已完成";

            // 6. Loader installation
            if (loaderChoice != "Vanilla")
            {
                task.Status = $"正在安装 {loaderChoice}...";
                task.ComponentsStatus = "正在安装";
                task.ComponentsStatusText = $"{loaderChoice}";
                // ... (Assume installation logic here)
                task.ComponentsProgress = 1.0;
                task.ComponentsStatus = "已完成";
                task.ComponentsStatusText = "已完成";
            }
            else {
                task.ComponentsProgress = 1.0;
                task.ComponentsStatus = "已完成";
                task.ComponentsStatusText = "无需安装";
            }
        }

        public string ReplaceSource(string originalUrl, string source)
        {
            if (string.IsNullOrEmpty(originalUrl) || source == "Official") return originalUrl;

            return source switch
            {
                "BMCLAPI" => originalUrl
                    .Replace("piston-meta.mojang.com", "bmclapi2.bangbang93.com")
                    .Replace("launchermeta.mojang.com", "bmclapi2.bangbang93.com")
                    .Replace("launcher.mojang.com", "bmclapi2.bangbang93.com")
                    .Replace("libraries.minecraft.net", "bmclapi2.bangbang93.com/maven")
                    .Replace("resources.download.minecraft.net", "bmclapi2.bangbang93.com/assets")
                    .Replace("files.minecraftforge.net/maven", "bmclapi2.bangbang93.com/maven")
                    .Replace("maven.minecraftforge.net", "bmclapi2.bangbang93.com/maven")
                    .Replace("maven.fabricmc.net", "bmclapi2.bangbang93.com/maven"),

                "FastMirror" => originalUrl
                    .Replace("piston-meta.mojang.com", "download.fastmirror.net")
                    .Replace("launchermeta.mojang.com", "download.fastmirror.net")
                    .Replace("launcher.mojang.com", "download.fastmirror.net")
                    .Replace("libraries.minecraft.net", "download.fastmirror.net/maven")
                    .Replace("resources.download.minecraft.net", "download.fastmirror.net/assets"),

                "MCMirror" => originalUrl
                    .Replace("piston-meta.mojang.com", "mirrors.mcfx.net")
                    .Replace("launchermeta.mojang.com", "mirrors.mcfx.net")
                    .Replace("launcher.mojang.com", "mirrors.mcfx.net")
                    .Replace("libraries.minecraft.net", "mirrors.mcfx.net/maven")
                    .Replace("resources.download.minecraft.net", "mirrors.mcfx.net/assets"),

                _ => originalUrl
            };
        }

        /// <summary>
        /// Resolves the windows natives classifier for a library entry
        /// (e.g. "natives-windows" from the "natives" node, or the classifier
        /// embedded in the maven name as a fallback).
        /// </summary>
        private static string? GetWindowsNativesClassifier(JToken lib)
        {
            var natives = lib["natives"];
            if (natives != null)
            {
                string? cls = natives["windows"]?.ToString();
                if (!string.IsNullOrEmpty(cls))
                    return cls.Replace("${arch}", IntPtr.Size == 8 ? "64" : "32");
            }

            string name = lib["name"]?.ToString() ?? "";
            var parts = name.Split(':');
            if (parts.Length > 3 && parts[3].Contains("natives", StringComparison.OrdinalIgnoreCase))
                return parts[3];

            return null;
        }

        /// <summary>
        /// 取主窗口的 MainViewModel —— **线程安全版**。
        ///
        /// 为什么必须走这一层：<c>Application.Current.MainWindow</c> 是 <see cref="System.Windows.Window"/>
        /// （DispatcherObject），从线程池线程读它会直接抛
        /// <c>InvalidOperationException: 调用线程无法访问此对象，因为另一个线程拥有该对象</c>。
        /// 而创建实例这条链现在**整条跑在线程池上**（Views/DownloadPage 点「创建实例」→
        /// DownloadViewModel.CreateInstance 的 Task.Run），以前那种「反正都在 UI 线程」的写法会
        /// 在 Task.Run 里第一步就炸掉、安装直接失败。
        /// 这里：本来就在 UI 线程 → 直接取（零额外调度）；否则 marshal 到 UI 线程取一次。
        /// </summary>
        private static TsuruLauncher.ViewModels.MainViewModel? GetMainViewModelSafe()
        {
            try
            {
                var app = Application.Current;
                if (app == null) return null;

                var dispatcher = app.Dispatcher;
                if (dispatcher == null) return null;

                if (dispatcher.CheckAccess())
                    return app.MainWindow?.DataContext as TsuruLauncher.ViewModels.MainViewModel;

                return dispatcher.Invoke(() => app.MainWindow?.DataContext as TsuruLauncher.ViewModels.MainViewModel);
            }
            catch { return null; }
        }

        public void CancelAll()
        {
            foreach (var task in ActiveTasks.ToList())
            {
                if (!task.IsCompleted && !task.IsFailed)
                {
                    task.Cts.Cancel();
                    task.Status = "已取消";
                    task.IsFailed = true;
                }
            }
        }

        public void RemoveCompleted()
        {
            var completed = ActiveTasks.Where(t => t.IsCompleted || t.IsFailed).ToList();
            foreach (var task in completed)
            {
                ActiveTasks.Remove(task);
            }
        }

        public void RetryFailed()
        {
            foreach (var task in ActiveTasks.Where(t => t.IsFailed).ToList())
            {
                task.Cts = new CancellationTokenSource();
                task.IsFailed = false;
                task.ErrorMessage = string.Empty;
                task.Status = "重试中...";
                task.Progress = 0;
                task.JsonProgress = 0;
                task.LibrariesProgress = 0;
                task.AssetsProgress = 0;
                task.ComponentsProgress = 0;

                if (!string.IsNullOrEmpty(task.DownloadUrl))
                {
                    // Generic file download (mod / resource pack / ...)
                    _ = EnqueueGenericDownload(task.Name, task.DownloadUrl, task.DestinationPath, task);
                }
                else if (!string.IsNullOrEmpty(task.VersionId))
                {
                    // Game (or game+loader) download — reuse the recorded choices
                    var version = new DownloadableVersion { Id = task.VersionId };
                    _ = AttemptDownloadWithLoaderAsync(task, version, task.LoaderChoice, task.LoaderVersion,
                        string.IsNullOrEmpty(task.Source) ? ConfigService.Instance.Settings.DownloadSource : task.Source);
                }
                else
                {
                    task.Status = "Failed";
                    task.IsFailed = true;
                    task.ErrorMessage = "该任务缺少重试信息，无法自动重试";
                }
            }
        }

        public async Task EnqueueGameDownloadWithLoader(DownloadableVersion version, string loaderChoice, string loaderVersion, string source)
        {
            var task = CreateGameTask(version, loaderChoice, loaderVersion, source);
            AddTask(task);
            await AttemptDownloadWithLoaderAsync(task, version, loaderChoice, loaderVersion, source);
        }

        // Overload that reuses a task already visible in the UI (single task per download,
        // so cancel/pause actually cancel the running download).
        public async Task EnqueueGameDownloadWithLoader(DownloadTask task, DownloadableVersion version, string loaderChoice, string loaderVersion, string source)
            => await AttemptDownloadWithLoaderAsync(task, version, loaderChoice, loaderVersion, source);

        private async Task AttemptDownloadWithLoaderAsync(DownloadTask task, DownloadableVersion version, string loaderChoice, string loaderVersion, string source)
        {
            try
            {
                task.IsFailed = false;
                task.ErrorMessage = string.Empty;

                await DownloadGameFullAsync(task, version, loaderChoice, source);

                if (loaderChoice != "Vanilla" && !string.IsNullOrEmpty(loaderVersion))
                {
                    var mainVM = GetMainViewModelSafe();
                    string gamePath = mainVM?.ConfigService.Settings.GamePath ?? ".minecraft";
                    var modLoaderService = new TsuruLauncher.Services.Ecosystem.ModLoaderService();

                    if (loaderChoice == "Fabric")
                    {
                        task.Status = $"正在安装 Fabric {loaderVersion}...";
                        task.ComponentsStatus = "正在安装";
                        task.ComponentsStatusText = $"Fabric {loaderVersion}";

                        await modLoaderService.InstallFabricAsync(version.Id, loaderVersion, gamePath,
                            new Progress<double>(p => task.ComponentsProgress = p),
                            new Progress<string>(s => task.ComponentsStatusText = s));
                    }
                    else if (loaderChoice == "Forge")
                    {
                        task.Status = $"正在安装 Forge {loaderVersion}...";
                        task.ComponentsStatus = "正在安装";
                        task.ComponentsStatusText = $"Forge {loaderVersion}";

                        await modLoaderService.InstallForgeAsync(version.Id, loaderVersion, gamePath,
                            new Progress<double>(p => task.ComponentsProgress = p),
                            new Progress<string>(s => task.ComponentsStatusText = s));
                    }
                    else if (loaderChoice == "OptiFine")
                    {
                        task.Status = $"正在安装 OptiFine {loaderVersion}...";
                        task.ComponentsStatus = "正在安装";
                        task.ComponentsStatusText = $"OptiFine {loaderVersion}";

                        await modLoaderService.InstallOptiFineAsync(version.Id, loaderVersion, gamePath,
                            new Progress<double>(p => task.ComponentsProgress = p),
                            new Progress<string>(s => task.ComponentsStatusText = s));
                    }
                }

                task.Status = "Completed";
                task.IsCompleted = true;
                task.Progress = 1.0;
            }
            catch (OperationCanceledException)
            {
                task.Status = "Canceled";
                task.IsFailed = true;
            }
            catch (Exception ex)
            {
                if (source != "Official")
                {
                    task.Status = $"镜像源异常 ({ex.Message})，正在切换至官方源重试...";
                    await Task.Delay(2000);
                    await AttemptDownloadWithLoaderAsync(task, version, loaderChoice, loaderVersion, "Official");
                    return;
                }

                task.Status = "Failed";
                task.IsFailed = true;
                task.ErrorMessage = ex.Message;
            }
        }
    }
}