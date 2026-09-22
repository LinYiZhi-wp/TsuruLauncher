using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TsuruLauncher.Models;
using TsuruLauncher.Services.Network;
using TsuruLauncher.Models.Ecosystem;

namespace TsuruLauncher.Services
{
    public class PreloadTask
    {
        public string Name { get; set; } = null!;
        public string Description { get; set; } = null!;
        public double Weight { get; set; } = 1.0;
        public Func<CancellationToken, Task> Action { get; set; } = null!;
        public bool IsCompleted { get; private set; }
        public bool HasError { get; private set; }
        public string? ErrorMessage { get; private set; }

        internal async Task ExecuteAsync(CancellationToken ct)
        {
            try
            {
                await Action(ct);
                IsCompleted = true;
            }
            catch (OperationCanceledException)
            {
                HasError = true;
                ErrorMessage = "Cancelled";
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = ex.Message;
            }
        }

        internal void Reset()
        {
            IsCompleted = false;
            HasError = false;
            ErrorMessage = null;
        }
    }

    public class PreloadProgressEventArgs : EventArgs
    {
        public string CurrentTask { get; set; } = "";
        public int CompletedTasks { get; set; }
        public int TotalTasks { get; set; }
        public double OverallProgress { get; set; }
        public bool IsComplete { get; set; }
    }

    public static class PreloadService
    {
        private static readonly List<PreloadTask> _tasks = new();
        private static CancellationTokenSource? _cts;
        private static int _completedCount;
        private static double _totalWeight;
        private static bool _isRunning;

        public static event EventHandler<PreloadProgressEventArgs>? ProgressChanged;

        public static bool IsPreloaded => _tasks.All(t => t.IsCompleted) && !_isRunning;
        public static IReadOnlyList<PreloadTask> Tasks => _tasks.AsReadOnly();

        static PreloadService()
        {
            RegisterDefaultTasks();
        }

        private static void RegisterDefaultTasks()
        {
            Register("VersionManifest", "正在获取版本列表", 2.0, async ct =>
            {
                var manifestService = new VersionManifestService();
                _cachedVersionList = await manifestService.GetVersionsAsync();
            });

            Register("ResourceData", "正在加载资源数据", 3.0, async ct =>
            {
                var modrinthService = new Ecosystem.ModrinthService();
                var trendingTask = modrinthService.GetTrendingAsync(6);
                var newestTask = modrinthService.GetNewestAsync(6);
                await Task.WhenAll(trendingTask, newestTask);

                _cachedTrendingMods = trendingTask.Result;
                _cachedNewestMods = newestTask.Result;

                var imageTasks = _cachedTrendingMods.Concat(_cachedNewestMods).Select(async mod =>
                {
                    if (!string.IsNullOrEmpty(mod.IconUrl) && !ct.IsCancellationRequested)
                    {
                        var img = await ImageCache.GetOrLoadAsync(mod.IconUrl, 180);
                        if (img != null && !ct.IsCancellationRequested)
                            mod.IconImage = img;
                    }
                });
                await Task.WhenAll(imageTasks);
            });
        }

        public static System.Windows.Media.Imaging.BitmapImage LoadDefaultBackground()
        {
            var bitmap = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/Images/cirno_bg.png"));
            bitmap.Freeze();
            return bitmap;
        }

        public static void Register(string name, string description, double weight, Func<CancellationToken, Task> action)
        {
            _tasks.Add(new PreloadTask
            {
                Name = name,
                Description = description,
                Weight = weight,
                Action = action
            });
            _totalWeight = _tasks.Sum(t => t.Weight);
        }

        public static async Task PreloadAllAsync(CancellationToken ct = default)
        {
            if (_isRunning) return;
            _isRunning = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _completedCount = 0;

            foreach (var task in _tasks) task.Reset();

            OnProgressChanged(new PreloadProgressEventArgs
            {
                CurrentTask = "",
                CompletedTasks = 0,
                TotalTasks = _tasks.Count,
                OverallProgress = 0,
                IsComplete = false
            });

            for (int i = 0; i < _tasks.Count; i++)
            {
                if (_cts.Token.IsCancellationRequested) break;

                var task = _tasks[i];
                OnProgressChanged(new PreloadProgressEventArgs
                {
                    CurrentTask = task.Description,
                    CompletedTasks = _completedCount,
                    TotalTasks = _tasks.Count,
                    OverallProgress = _tasks.Take(i).Sum(t => t.Weight) / Math.Max(_totalWeight, 1),
                    IsComplete = false
                });

                await task.ExecuteAsync(_cts.Token);
                _completedCount++;
            }

            OnProgressChanged(new PreloadProgressEventArgs
            {
                CurrentTask = "Ready",
                CompletedTasks = _completedCount,
                TotalTasks = _tasks.Count,
                OverallProgress = 1.0,
                IsComplete = true
            });

            _isRunning = false;
        }

        public static void Cancel() => _cts?.Cancel();

        private static void OnProgressChanged(PreloadProgressEventArgs e) => ProgressChanged?.Invoke(null, e);

        #region Cached Data

        private static readonly object _cacheLock = new object();
        private static volatile List<GameInstance> _cachedVersions = new();
        private static volatile List<DownloadableVersion> _cachedVersionList = new();
        private static volatile List<ModProject> _cachedTrendingMods = new();
        private static volatile List<ModProject> _cachedNewestMods = new();

        public static List<GameInstance> CachedVersions { get { lock (_cacheLock) { return _cachedVersions; } } }
        public static List<DownloadableVersion> CachedVersionList { get { lock (_cacheLock) { return _cachedVersionList; } } }
        public static List<ModProject> CachedTrendingMods { get { lock (_cacheLock) { return _cachedTrendingMods; } } }
        public static List<ModProject> CachedNewestMods { get { lock (_cacheLock) { return _cachedNewestMods; } } }

        #endregion
    }
}