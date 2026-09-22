using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TsuruLauncher.Controls;
using TsuruLauncher.Models;
using TsuruLauncher.Models.Ecosystem;
using TsuruLauncher.Services.Ecosystem;
using TsuruLauncher.Services.Network;
using TsuruLauncher.Services;
using TsuruLauncher.Views;

namespace TsuruLauncher.ViewModels
{
    public partial class ResourceDetailViewModel : ObservableObject
    {
        private readonly ContentSourceService _source;
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

        // ══════════════ 版本筛选（美西螈 VersionFilterControl：平台 / 游戏版本 / 通道）══════════════
        // 三个 MultiSelect 下拉 + 下面一排「已生效」胶囊。勾选变化由控件抛事件回来，
        // 这里只管把 FilterOption.IsSelected 读出来重新过滤。
        public ObservableCollection<FilterOption> PlatformFilters { get; } = new();
        public ObservableCollection<FilterOption> GameVersionFilters { get; } = new();
        public ObservableCollection<FilterOption> ChannelFilters { get; } = new();

        /// <summary>已生效的筛选胶囊（点 × 取消对应项）。</summary>
        public ObservableCollection<ActiveFilterChip> ActiveFilterChips { get; } = new();

        [ObservableProperty]
        private bool _showAllGameVersions;

        [ObservableProperty]
        private bool _hasActiveFilters;

        partial void OnShowAllGameVersionsChanged(bool value)
        {
            BuildGameVersionFilterOptions();
            ApplyFilters();
        }

        /// <summary>下拉里是否至少有两项 —— 只有一项时筛不筛都一样，美西螈会直接把这颗下拉藏掉。</summary>
        public bool ShowPlatformFilter => PlatformFilters.Count > 1;
        public bool ShowGameVersionFilter => GameVersionFilters.Count > 1;
        public bool ShowChannelFilter => ChannelFilters.Count > 1;

        // ══════════════ hero 的「翻译」══════════════
        [ObservableProperty]
        private bool _isTranslating;

        [ObservableProperty]
        private bool _isTranslated;

        /// <summary>翻译前的原文，用来支持「显示原文」回退。</summary>
        private string? _originalDescription;

        public string TranslateButtonText => IsTranslating ? "翻译中…" : IsTranslated ? "显示原文" : "翻译";

        partial void OnIsTranslatingChanged(bool value) => OnPropertyChanged(nameof(TranslateButtonText));
        partial void OnIsTranslatedChanged(bool value) => OnPropertyChanged(nameof(TranslateButtonText));

        // ─────────── 版本表格分页（Axolotl：每页 20 条 + 页码条）───────────
        public const int PageSize = 20;

        [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
        private int _currentPage = 1;

        /// <summary>总页数（至少 1）。</summary>
        public int TotalPages => Math.Max(1, (int)Math.Ceiling((FilteredVersions?.Count ?? 0) / (double)PageSize));

        /// <summary>当前页的版本 —— 表格绑这个，不直接绑 FilteredVersions。</summary>
        public List<ModFile> PagedVersions =>
            (FilteredVersions ?? new List<ModFile>())
                .Skip((Math.Max(1, CurrentPage) - 1) * PageSize).Take(PageSize).ToList();

        /// <summary>页码条要显示的页码（超过 7 页时只给前 5 页 + 末页）。</summary>
        public List<PageNumberItem> PageNumbers
        {
            get
            {
                int total = TotalPages;
                var list = new List<PageNumberItem>();
                void Add(int n) => list.Add(new PageNumberItem { Number = n, IsCurrent = n == CurrentPage });

                if (total <= 7) { for (int i = 1; i <= total; i++) Add(i); return list; }
                for (int i = 1; i <= 5; i++) Add(i);
                Add(total);
                return list;
            }
        }

        public bool CanPrevPage => CurrentPage > 1;
        public bool CanNextPage => CurrentPage < TotalPages;

        /// <summary>当前筛选结果里是否有版本（控制空状态文案 / 表头显示）。</summary>
        public bool HasVersions => (FilteredVersions?.Count ?? 0) > 0;

        partial void OnCurrentPageChanged(int value) => NotifyPaging();

        partial void OnFilteredVersionsChanged(List<ModFile> value) => OnPropertyChanged(nameof(HasVersions));

        private void NotifyPaging()
        {
            OnPropertyChanged(nameof(PagedVersions));
            OnPropertyChanged(nameof(TotalPages));
            OnPropertyChanged(nameof(PageNumbers));
            OnPropertyChanged(nameof(CanPrevPage));
            OnPropertyChanged(nameof(CanNextPage));
            OnPropertyChanged(nameof(HasVersions));
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void GoPage(int page)
        {
            if (page < 1 || page > TotalPages) return;
            CurrentPage = page;
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void NextPage() => GoPage(CurrentPage + 1);

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void PrevPage() => GoPage(CurrentPage - 1);
        public List<DownloadSource> SourceOptions { get; } = Enum.GetValues(typeof(DownloadSource)).Cast<DownloadSource>().ToList();

        public ResourceDetailViewModel()
        {
            _source = new ContentSourceService();
            _downloadManager = DownloadManagerService.Instance;
        }

        /// <summary>
        /// 自检专用：模拟「从某个游戏版本的实例打开一个**不支持该版本**的资源」。
        /// 回归用户报的「版本列表显示未找到匹配版本」——以前 ApplyFilters 会把
        /// TargetGameVersion 强制塞进筛选条件，1.8.9 实例打开 Fabric API 就筛成空。
        /// </summary>
        public (int Total, int Filtered) SelfTestApplyTargetGameVersion(string gameVersion)
        {
            TargetGameVersion = gameVersion;
            BuildFilterOptions();
            PreselectTargetGameVersion();
            ApplyFilters();
            return (Resource?.Versions.Count ?? 0, FilteredVersions?.Count ?? 0);
        }

        public async Task InitializeAsync(ModProject project, string? gameVersion = null)
        {
            SourceProject = project;
            TargetGameVersion = gameVersion;
            _source.Platform = project.Platform;
            
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
                    : _source.GetProjectDetailAsync(project.Id, project.Platform);
                // ⚠ 这里**不带** gameVersion 去请求：美西螈是拉全量版本、再由客户端的
                //    「Game versions」下拉筛。若服务端先筛掉，下拉里就只剩一个版本可勾，
                //    筛选器形同虚设（之前就是这个毛病）。目标版本改为预先勾中。
                var versionsTask = _source.GetVersionsAsync(project.Id, null, null, project.Platform);

                await Task.WhenAll(detailTask, versionsTask);

                var detail = await detailTask;
                var versions = await versionsTask;

                if (detail != null)
                {
                    Resource = detail;

                    // ⚠ 上面 `Resource = detail` 会把初始化时从列表项带过来的 IconImage 丢掉
                    //   （detail 是新对象，IconImage 是 null），而 Modrinth 的详情接口只给
                    //   icon_url 不给位图 —— 所以图标一直是那块空占位。这里补回来 + 异步拉。
                    if (detail.IconImage == null) detail.IconImage = project.IconImage;
                    _ = LoadIconAsync(detail);

                    FillAuthorFallback(detail);
                    _ = LoadAuthorAvatarsAsync(detail);
                    _ = LoadGalleryImagesAsync(detail);

                    if (versions != null)
                    {
                        Resource.Versions = versions;
                        FilteredVersions = versions.OrderByDescending(v => v.ReleaseDate).ToList();
                        CurrentPage = 1;
                        NotifyPaging();
                        // 三个筛选下拉的候选集来自版本列表，必须在版本到位之后再建
                        BuildFilterOptions();
                        PreselectTargetGameVersion();
                        UpdateInstallPath();
                        ApplyFilters();
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

        /// <summary>
        /// 从资源页带着目标游戏版本进来时，把它在「游戏版本」下拉里预先勾上
        /// （美西螈进详情页也是这个效果：目标版本已经是选中态）。
        /// 目标版本如果是快照/预发布，默认列表里没有 → 先打开「显示全部版本」。
        /// </summary>
        private void PreselectTargetGameVersion()
        {
            if (string.IsNullOrWhiteSpace(TargetGameVersion)) return;

            // 先在当前候选里找（默认只含正式版）。
            var match = FindGameVersionOption(TargetGameVersion!);
            if (match == null && !ShowAllGameVersions)
            {
                // 找不到就放开「显示全部版本」再找一次（目标版本可能是快照）。
                bool prev = ShowAllGameVersions;
                ShowAllGameVersions = true;               // 会重建候选集
                match = FindGameVersionOption(TargetGameVersion!);
                if (match == null)
                {
                    // 资源**根本不支持**这个版本（如 1.8.9 实例打开 Fabric API）：
                    // 恢复原状，别把「显示全部版本」和快照列表留在界面上。
                    ShowAllGameVersions = prev;
                    return;
                }
            }
            if (match != null) match.IsSelected = true;
        }

        private FilterOption? FindGameVersionOption(string value)
            => GameVersionFilters.FirstOrDefault(o =>
                   string.Equals(o.Value, value, StringComparison.OrdinalIgnoreCase));

        // ═══════════════════════ 筛选项的构建 ═══════════════════════

        /// <summary>版本拉回来后重建三个下拉的候选集。已勾选的值会保留（避免重建后用户的选择被清空）。</summary>
        private void BuildFilterOptions()
        {
            var versions = Resource?.Versions ?? new List<ModFile>();

            BuildPlatformFilterOptions(versions);
            BuildGameVersionFilterOptions();
            BuildChannelFilterOptions(versions);

            OnPropertyChanged(nameof(ShowPlatformFilter));
            OnPropertyChanged(nameof(ShowGameVersionFilter));
            OnPropertyChanged(nameof(ShowChannelFilter));
        }

        private void BuildPlatformFilterOptions(List<ModFile> versions)
        {
            var selected = PlatformFilters.Where(o => o.IsSelected).Select(o => o.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var loaders = versions
                .SelectMany(v => v.Loaders ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(NormalizeLoader)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => LoaderOrder(s))
                .ThenBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            PlatformFilters.Clear();
            foreach (var l in loaders)
                PlatformFilters.Add(new FilterOption(l, l, selected.Contains(l)));
        }

        /// <summary>
        /// 游戏版本下拉。默认只列**正式版**（美西螈也是先只给正式版，底部的
        /// 「显示全部版本」打开后才把快照/预发布放进来）—— 否则 Fabric API 这种
        /// 600+ 版本的项会把下拉撑爆。
        /// </summary>
        private void BuildGameVersionFilterOptions()
        {
            var versions = Resource?.Versions ?? new List<ModFile>();
            var selected = GameVersionFilters.Where(o => o.IsSelected).Select(o => o.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var all = versions
                .SelectMany(v => v.GameVersions ?? new List<string>())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            if (!ShowAllGameVersions)
                all = all.Where(IsReleaseVersion);

            var list = all.ToList();
            list.Sort((a, b) => CompareGameVersion(b, a));   // 新 → 旧

            GameVersionFilters.Clear();
            foreach (var g in list)
                GameVersionFilters.Add(new FilterOption(g, g, selected.Contains(g)));
        }

        private void BuildChannelFilterOptions(List<ModFile> versions)
        {
            var selected = ChannelFilters.Where(o => o.IsSelected).Select(o => o.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var present = versions.Select(v => v.VersionType ?? "release")
                                  .ToHashSet(StringComparer.OrdinalIgnoreCase);

            ChannelFilters.Clear();
            // 固定顺序：正式版 → 测试版 → 内测版（跟美西螈 release/beta/alpha 一致）
            foreach (var ch in new[] { "release", "beta", "alpha" })
            {
                if (!present.Contains(ch)) continue;
                ChannelFilters.Add(new FilterOption(ch, ch switch
                {
                    "beta" => "测试版",
                    "alpha" => "内测版",
                    _ => "正式版"
                }, selected.Contains(ch)));
            }
        }

        /// <summary>把 Modrinth / CurseForge 的 loader 名统一成显示用的写法。</summary>
        private static string NormalizeLoader(string raw) => raw.ToLowerInvariant() switch
        {
            "neoforge" => "NeoForge",
            "fabric" => "Fabric",
            "forge" => "Forge",
            "quilt" => "Quilt",
            "liteloader" => "LiteLoader",
            "rift" => "Rift",
            "iris" => "Iris",
            "optifine" => "OptiFine",
            "canvas" => "Canvas",
            "modloader" => "ModLoader",
            "bukkit" => "Bukkit",
            "spigot" => "Spigot",
            "paper" => "Paper",
            "purpur" => "Purpur",
            "folia" => "Folia",
            "velocity" => "Velocity",
            "waterfall" => "Waterfall",
            "bungeecord" => "BungeeCord",
            "datapack" => "数据包",
            _ => raw
        };

        private static int LoaderOrder(string loader) => loader switch
        {
            "Fabric" => 0,
            "NeoForge" => 1,
            "Forge" => 2,
            "Quilt" => 3,
            _ => 9
        };

        /// <summary>正式版判定：没有 <c>-pre</c>/<c>-rc</c> 后缀，也不是快照（<c>24w14a</c>）。</summary>
        private static bool IsReleaseVersion(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return false;
            if (v.Contains('-')) return false;
            return !System.Text.RegularExpressions.Regex.IsMatch(v, @"^\d{2}w\d{2}");
        }

        /// <summary>
        /// 游戏版本排序键（用于「新 → 旧」）。数字段按数值比，快照按 <c>年*100+周</c> 比，
        /// 预发布排在对应正式版之后。
        /// </summary>
        private static long GameVersionSortKey(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return long.MinValue;

            var snapshot = System.Text.RegularExpressions.Regex.Match(v, @"^(\d{2})w(\d{2})");
            if (snapshot.Success)
                return long.Parse(snapshot.Groups[1].Value) * 100_000L + long.Parse(snapshot.Groups[2].Value) * 100L;

            var parts = v.Split('-')[0].Split('.');
            long key = 0;
            for (int i = 0; i < parts.Length && i < 3; i++)
            {
                if (!int.TryParse(parts[i], out int n)) n = 0;
                key = key * 1000L + n;
            }
            // 预发布 (-pre/-rc) 略低于同名正式版
            if (v.Contains("-pre") || v.Contains("-rc")) key = key * 10L - 1;
            else key *= 10L;
            return key;
        }

        private static int CompareGameVersion(string a, string b)
            => GameVersionSortKey(a).CompareTo(GameVersionSortKey(b));

        private void ApplyFilters()
        {
            if (Resource == null) return;

            var versions = Resource.Versions.AsEnumerable();

            // ① 游戏版本：**只**用下拉里真正勾上的那些。
            //    ⚠ 之前这里还会把 TargetGameVersion（进详情页时带的「我当前的实例版本」）强制
            //    插进筛选条件 —— 于是「1.8.9 实例 + Fabric API（只支持 1.14+）」这种组合会把
            //    版本列表筛成空，界面显示「没有找到匹配的版本」（用户报的 bug）。
            //    正确做法：TargetGameVersion 只作为**预选偏好**（见 PreselectTargetGameVersion，
            //    只在资源确实有这个版本时才勾上），绝不强制参与筛选。
            var gvSelected = GameVersionFilters.Where(o => o.IsSelected).Select(o => o.Value).ToList();
            if (gvSelected.Count > 0)
            {
                versions = versions.Where(v =>
                    v.GameVersions.Count == 0 ||
                    gvSelected.Any(g => v.GameVersions.Contains(g, StringComparer.OrdinalIgnoreCase)));
            }

            // ② 平台（loader）
            var pfSelected = PlatformFilters.Where(o => o.IsSelected).Select(o => o.Value).ToList();
            if (pfSelected.Count > 0)
            {
                versions = versions.Where(v =>
                    v.Loaders.Count == 0 ||
                    pfSelected.Any(l => v.Loaders.Contains(l, StringComparer.OrdinalIgnoreCase)));
            }

            // ③ 发布通道
            var chSelected = ChannelFilters.Where(o => o.IsSelected).Select(o => o.Value).ToList();
            if (chSelected.Count > 0)
            {
                versions = versions.Where(v =>
                    chSelected.Contains(v.VersionType ?? "release", StringComparer.OrdinalIgnoreCase));
            }

            // ④ 兼容旧的单选 loader 过滤（新 UI 不再用，留着防止别处仍在设它）
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

            FilteredVersions = versions.ToList();
            CurrentPage = 1;
            NotifyPaging();
            RebuildFilterChips();

            if (FilteredVersions.Count > 0)
            {
                // 选中的版本被筛掉了就重新挑一个最合适的
                if (Resource.SelectedVersion == null || !FilteredVersions.Contains(Resource.SelectedVersion))
                    SelectBestMatchVersion();
            }
            else
            {
                Resource.SelectedVersion = null;
                SelectedVersionItem = null;
                DependencyList.Clear();
                ShowDependenciesPanel = false;
            }
        }

        /// <summary>把当前勾选项摊平成下面那排胶囊。</summary>
        private void RebuildFilterChips()
        {
            ActiveFilterChips.Clear();

            foreach (var o in PlatformFilters.Where(o => o.IsSelected))
                ActiveFilterChips.Add(new ActiveFilterChip
                {
                    Label = o.Label,
                    Group = "platform",
                    Value = o.Value,
                    RemoveCommand = new RelayCommand(() => o.IsSelected = false)
                });

            foreach (var o in GameVersionFilters.Where(o => o.IsSelected))
                ActiveFilterChips.Add(new ActiveFilterChip
                {
                    Label = o.Label,
                    Group = "gameVersion",
                    Value = o.Value,
                    RemoveCommand = new RelayCommand(() => o.IsSelected = false)
                });

            foreach (var o in ChannelFilters.Where(o => o.IsSelected))
                ActiveFilterChips.Add(new ActiveFilterChip
                {
                    Label = o.Label,
                    Group = "channel",
                    Value = o.Value,
                    RemoveCommand = new RelayCommand(() => o.IsSelected = false)
                });

            HasActiveFilters = ActiveFilterChips.Count > 0;
            OnPropertyChanged(nameof(ActiveFilterChips));
        }

        /// <summary>三个下拉里任意一项的勾选变了。</summary>
        [RelayCommand]
        private void FiltersChanged() => ApplyFilters();

        [RelayCommand]
        private void ClearAllFilters()
        {
            foreach (var o in PlatformFilters) o.IsSelected = false;
            foreach (var o in GameVersionFilters) o.IsSelected = false;
            foreach (var o in ChannelFilters) o.IsSelected = false;
            ApplyFilters();
        }

        /// <summary>「显示全部版本」开关（快照 / 预发布）—— UI 直接双向绑 ShowAllGameVersions，
        /// 属性 setter 里已经重建候选集并重筛，这条命令留给需要显式触发的场合。</summary>
        [RelayCommand]
        private void ToggleShowAllVersions() => ShowAllGameVersions = !ShowAllGameVersions;

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

            // PCL2-style: let the user pick which version the mod goes into
            // (or download it without installing)
            var pickResult = ModInstallPicker.Show(
                Application.Current.MainWindow,
                mainVM?.GameVersions?.ToList() ?? new List<GameInstance>(),
                mainVM?.SelectedVersion,
                Resource.Name);

            if (pickResult.Cancelled) return;

            string targetDir;
            if (pickResult.Target != null)
            {
                targetDir = pickResult.Target.GameDir;
                if (string.IsNullOrEmpty(targetDir)) targetDir = gamePath;
            }
            else
            {
                // Download-only: save into the user's Downloads/TsuruLauncher folder
                targetDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Downloads", "TsuruLauncher");
            }

            string destDir = GetDestinationDirectory(targetDir);
            Directory.CreateDirectory(destDir);
            string destPath = Path.Combine(destDir, Resource.SelectedVersion.FileName);

            // Show notification
            string where = pickResult.Target != null ? pickResult.Target.Id : "仅下载（未安装）";
            mainVM?.NotificationService.ShowSuccess("下载任务", $"已加入下载任务：{Resource.Name} → {where}");

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

                    if (pickResult.DownloadDependencies && Resource.SelectedVersion.Dependencies?.Count > 0)
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
                    var depVersions = await _source.GetVersionsAsync(dep.ProjectId, TargetGameVersion, null, Resource.Platform);
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

        /// <summary>
        /// 某个版本在平台网站上的地址（版本表格那一行的 ↗）。
        /// Modrinth：<c>{项目页}/version/{versionId}</c>；CurseForge 没有稳定深链，退回项目页。
        /// </summary>
        public string BuildVersionWebUrl(ModFile? file)
        {
            if (Resource == null) return string.Empty;
            if (file == null || string.IsNullOrWhiteSpace(file.FileId)) return Resource.WebUrl;

            if (Resource.Platform == ProjectPlatform.Modrinth && !string.IsNullOrWhiteSpace(Resource.WebUrl))
                return Resource.WebUrl.TrimEnd('/') + "/version/" + file.FileId;

            return Resource.WebUrl;
        }

        [RelayCommand]
        private void CopyDownloadLink()        {
            if (Resource?.SelectedVersion?.DownloadUrl != null)
            {
                try { Clipboard.SetText(Resource.SelectedVersion.DownloadUrl); }
                catch { }
            }
        }

        // ══════════════ hero 的「翻译 / 安装 / ⋮」三颗按钮 ══════════════

        /// <summary>
        /// 翻译项目简介（美西螈 hero 的 LanguagesIcon 那颗）。再点一次切回原文。
        /// 走 <see cref="TranslationService"/>（MyMemory 免密钥接口），失败就给个提示，
        /// 不弹异常、不动原文。
        /// </summary>
        [RelayCommand]
        private async Task ToggleTranslationAsync()
        {
            if (Resource == null || IsTranslating) return;

            if (IsTranslated)
            {
                // 切回原文
                if (_originalDescription != null)
                    Resource.Description = _originalDescription;
                IsTranslated = false;
                return;
            }

            IsTranslating = true;
            try
            {
                _originalDescription = Resource.Description;
                string? translated = await TranslationService.TranslateToChineseAsync(Resource.Description);

                if (string.IsNullOrWhiteSpace(translated))
                {
                    Notify("翻译失败", "翻译服务暂时不可用，请稍后再试");
                    _originalDescription = null;
                    return;
                }

                Resource.Description = translated!;
                IsTranslated = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ResourceDetail] Translate error: {ex.Message}");
                _originalDescription = null;
                Notify("翻译失败", ex.Message);
            }
            finally
            {
                IsTranslating = false;
            }
        }

        /// <summary>hero 的「⋮」里「在浏览器中打开」已有单独命令，这里补「复制链接」和「举报」。</summary>
        public string ReportUrl
        {
            get
            {
                if (Resource == null) return string.Empty;
                if (Resource.Platform == ProjectPlatform.Modrinth)
                    return $"https://modrinth.com/report?item=project&itemID={Resource.Id}";
                return Resource.WebUrl;
            }
        }

        public bool CanReport => !string.IsNullOrWhiteSpace(ReportUrl);

        [RelayCommand]
        private void ReportProject()
        {
            string url = ReportUrl;
            if (string.IsNullOrWhiteSpace(url)) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { }
        }

        private void Notify(string title, string message)
        {
            try
            {
                var mainVM = ((App)Application.Current).MainWindow?.DataContext as MainViewModel;
                mainVM?.NotificationService.ShowError(title, message);
            }
            catch { }
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
            TsuruLauncher.Converters.DataContextHelper.CurrentResource = value;
            OnPropertyChanged(nameof(ReportUrl));
            OnPropertyChanged(nameof(CanReport));
        }
        /// <summary>
        /// 异步拉项目图标。
        /// Modrinth 的 <c>/project/{id}</c> 只给 <c>icon_url</c>，不给位图；CurseForge 同理。
        /// 不主动拉的话 hero 左上角永远是那块空占位方块（实测截图确认）。
        /// </summary>
        private static async System.Threading.Tasks.Task LoadIconAsync(ResourceDetail detail)
        {
            try
            {
                if (detail.IconImage != null || string.IsNullOrWhiteSpace(detail.IconUrl)) return;
                var img = await Services.ImageCache.GetOrLoadAsync(detail.IconUrl, 160).ConfigureAwait(false);
                if (img == null) return;
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => detail.IconImage = img);
            }
            catch { }
        }

        /// <summary>
        /// 作者名的兜底。
        /// Modrinth 的 <c>/project/{id}</c> **不返回**嵌套的 <c>team.members[0].user.username</c>，
        /// 所以 <c>Author</c> 解析出来是空串 —— hero 上就只剩一个孤零零的 "by"。
        /// 团队名公开 API 拿不到（<c>/team/{id}</c> 是 404），退而取 Project Lead。
        /// </summary>
        private static void FillAuthorFallback(ResourceDetail detail)
        {
            if (!string.IsNullOrWhiteSpace(detail.Author)) return;
            if (detail.Authors == null || detail.Authors.Count == 0) return;

            var lead = detail.Authors.FirstOrDefault(a =>
                           a.Role.Contains("Lead", StringComparison.OrdinalIgnoreCase))
                       ?? detail.Authors.FirstOrDefault(a =>
                           string.Equals(a.Role, "Owner", StringComparison.OrdinalIgnoreCase))
                       ?? detail.Authors[0];
            detail.Author = lead.Name;
        }

        /// <summary>
        /// 异步拉图库图片。
        /// 之前直接把 URL 字符串绑到 <c>Image.Source</c> —— WPF 会在 UI 线程同步下载，
        /// 实测 4 张里有 2 张空白（下载失败/超时且没有兜底），而且拖慢整页。
        /// 现在统一走 ImageCache，跟图标 / 作者头像同一条路。
        /// </summary>
        private static async System.Threading.Tasks.Task LoadGalleryImagesAsync(ResourceDetail detail)
        {
            try
            {
                foreach (var img in detail.GalleryImages)
                {
                    if (img.Image != null || string.IsNullOrWhiteSpace(img.Url)) continue;
                    var bmp = await Services.ImageCache.GetOrLoadAsync(img.Url, 480).ConfigureAwait(false);
                    if (bmp == null) continue;
                    var target = img;
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => target.Image = bmp);
                }
            }
            catch { }
        }

        /// <summary>异步拉作者头像（CurseForge 不返回头像，会保持首字母占位圆底）。</summary>
        private static async System.Threading.Tasks.Task LoadAuthorAvatarsAsync(ResourceDetail detail)        {
            try
            {
                foreach (var a in detail.Authors)
                {
                    if (string.IsNullOrWhiteSpace(a.AvatarUrl)) continue;
                    var img = await Services.ImageCache.GetOrLoadAsync(a.AvatarUrl, 60).ConfigureAwait(false);
                    if (img == null) continue;
                    var author = a;
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => author.AvatarImage = img);
                }
            }
            catch { }
        }

    }

    /// <summary>页码条上的一格。当前页要高亮，所以不能只传一个 int。</summary>
    public class PageNumberItem
    {
        public int Number { get; set; }
        public bool IsCurrent { get; set; }
    }
}