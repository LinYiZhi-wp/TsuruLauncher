using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TsuruLauncher.Models;
using TsuruLauncher.Models.Ecosystem;
using TsuruLauncher.Services.Ecosystem;
using TsuruLauncher.Services;
using TsuruLauncher.Services.Network;
using TsuruLauncher.Controls;

namespace TsuruLauncher.ViewModels
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

    /// <summary>
    /// 列表视图的行显示项：ModProject 本身只有列表级字段，
    /// 标签（加载器 / 游戏版本）、更新时间、运行环境由详情按需补齐。
    /// </summary>
    public class ResourceRowItem : ObservableObject
    {
        private string _updatedText = string.Empty;
        private bool _isEnriching;
        private bool _isEnriched;
        private bool _hasEnvironmentInfo;
        private bool _isClientSideOnly;
        private bool _isServerSideOnly;

        public ResourceRowItem(ModProject project)
        {
            Project = project;

            // 详情还没回来之前先用内容类型 + 内容源占位，行卡片不会空着
            Tags.Add(TypeLabel(project.Type));
            Tags.Add(PlatformLabel(project.Platform));
        }

        public ModProject Project { get; }

        public ObservableCollection<string> Tags { get; } = new ObservableCollection<string>();

        public string Name => Project.Name;
        public string Summary => Project.Summary;
        public string Author => Project.Author;
        public long Downloads => Project.Downloads;
        public System.Windows.Media.Imaging.BitmapImage? IconImage => Project.IconImage;
        public string PlatformText => PlatformLabel(Project.Platform);

        /// <summary>例如「3 天前更新」，详情回来后才有值。</summary>
        public string UpdatedText { get => _updatedText; private set => SetProperty(ref _updatedText, value); }

        public bool IsEnriching { get => _isEnriching; set => SetProperty(ref _isEnriching, value); }
        public bool IsEnriched { get => _isEnriched; private set => SetProperty(ref _isEnriched, value); }
        public bool HasEnvironmentInfo { get => _hasEnvironmentInfo; private set => SetProperty(ref _hasEnvironmentInfo, value); }
        public bool IsClientSideOnly { get => _isClientSideOnly; private set => SetProperty(ref _isClientSideOnly, value); }
        public bool IsServerSideOnly { get => _isServerSideOnly; private set => SetProperty(ref _isServerSideOnly, value); }

        /// <summary>缩略图是异步补的，加载完通知一次界面。</summary>
        public void NotifyIconChanged() => OnPropertyChanged(nameof(IconImage));

        /// <summary>详情返回后补齐标签（加载器 / 游戏版本各取前 3）与更新时间。</summary>
        public void ApplyDetail(ResourceDetail detail)
        {
            if (detail == null) return;

            Tags.Clear();
            foreach (var loader in detail.Loaders.Where(s => !string.IsNullOrWhiteSpace(s)).Take(3))
                Tags.Add(LoaderLabel(loader));
            foreach (var version in detail.GameVersions.Where(s => !string.IsNullOrWhiteSpace(s)).Take(3))
                Tags.Add(version);

            if (Tags.Count == 0)
            {
                Tags.Add(TypeLabel(detail.Type));
                Tags.Add(PlatformLabel(detail.Platform));
            }

            UpdatedText = DescribeUpdated(detail.DateModified);

            if (!string.IsNullOrWhiteSpace(detail.Author) && string.IsNullOrWhiteSpace(Project.Author))
            {
                Project.Author = detail.Author;
                OnPropertyChanged(nameof(Author));
            }

            if (!string.IsNullOrWhiteSpace(detail.Summary) && string.IsNullOrWhiteSpace(Project.Summary))
            {
                Project.Summary = detail.Summary;
                OnPropertyChanged(nameof(Summary));
            }

            if (detail.Downloads > Project.Downloads)
            {
                Project.Downloads = detail.Downloads;
                OnPropertyChanged(nameof(Downloads));
            }

            IsClientSideOnly = detail.IsClientSideOnly;
            IsServerSideOnly = detail.IsServerSideOnly;
            HasEnvironmentInfo = detail.IsClientSideOnly || detail.IsServerSideOnly;
            IsEnriched = true;
        }

        public static string TypeLabel(ProjectType type) => type switch
        {
            ProjectType.Modpack => "整合包",
            ProjectType.ResourcePack => "资源包",
            ProjectType.Shader => "光影",
            ProjectType.DataPack => "数据包",
            _ => "模组"
        };

        public static string PlatformLabel(Models.Ecosystem.ProjectPlatform platform)
            => platform == Models.Ecosystem.ProjectPlatform.CurseForge ? "CurseForge" : "Modrinth";

        public static string LoaderLabel(string loader) => loader.ToLowerInvariant() switch
        {
            "fabric" => "Fabric",
            "forge" => "Forge",
            "neoforge" => "NeoForge",
            "quilt" => "Quilt",
            "liteloader" => "LiteLoader",
            "rift" => "Rift",
            "client" => "客户端",
            "server" => "服务端",
            _ => loader
        };

        /// <summary>把详情里的更新时间转成「N 天前更新」。</summary>
        public static string DescribeUpdated(DateTime date)
        {
            if (date == default) return string.Empty;

            var utc = date.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(date, DateTimeKind.Utc)
                : date.ToUniversalTime();

            var span = DateTime.UtcNow - utc;
            if (span.TotalMinutes < 5) return "刚刚更新";
            if (span.TotalHours < 1) return $"{(int)span.TotalMinutes} 分钟前更新";
            if (span.TotalDays < 1) return $"{(int)span.TotalHours} 小时前更新";
            if (span.TotalDays < 30) return $"{(int)span.TotalDays} 天前更新";
            if (span.TotalDays < 365) return $"{(int)(span.TotalDays / 30)} 个月前更新";
            return $"{(int)(span.TotalDays / 365)} 年前更新";
        }
    }

    /// <summary>分页条上的一格（省略号时 Number = 0）。</summary>
    public class PageItem
    {
        public int Number { get; init; }
        public string Display { get; init; } = string.Empty;
        public bool IsEllipsis { get; init; }
        public bool IsCurrent { get; init; }
    }

    public partial class ResourcesViewModel : ObservableObject
    {
        private readonly ContentSourceService _source;
        private readonly ModpackService _modpackService;
        private readonly ConfigService _configService;
        private CancellationTokenSource? _searchDebounceCts;
        private CancellationTokenSource? _featuredLoadCts;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<ResourceDetail?>> _preloadCache = new();
        private static readonly SemaphoreSlim _enrichGate = new SemaphoreSlim(3, 3);

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

        /// <summary>列表 / 网格 显示切换（对齐 Axolotl 的资源视图切换）。</summary>
        [ObservableProperty]
        private bool _isListView;

        /// <summary>0 = 相关度，1 = 下载量，2 = 最近更新</summary>
        [ObservableProperty]
        private int _sortIndex;

        public ObservableCollection<string> SortOptions { get; } = new() { "相关度", "下载量", "最近更新" };

        private string SortKey => SortIndex == 1 ? "downloads" : SortIndex == 2 ? "updated" : "relevance";

        /// <summary>每页条数（「查看数量」下拉：20 / 40 / 60）。</summary>
        [ObservableProperty]
        private int _pageSize = 20;

        public ObservableCollection<int> PageSizeOptions { get; } = new() { 20, 40, 60 };

        /// <summary>当前页（从 1 开始）。</summary>
        [ObservableProperty]
        private int _currentPage = 1;

        /// <summary>内容源不返回总数，先按已加载条数估算。</summary>
        [ObservableProperty]
        private int _totalPages = 1;

        [ObservableProperty]
        private bool _hasNextPage;

        /// <summary>右栏「类别」长列表是否展开。</summary>
        [ObservableProperty]
        private bool _isCategoryListExpanded = true;

        /// <summary>右栏「包含内容」搜索框：就地过滤类别长列表。</summary>
        [ObservableProperty]
        private string _contentFilterText = string.Empty;

        /// <summary>右栏类别长列表选中的子类别（中文标签，会折进搜索词）。</summary>
        [ObservableProperty]
        private string? _selectedTag;

        /// <summary>运行环境：全部 / 客户端 / 服务端。</summary>
        [ObservableProperty]
        private string _selectedEnvironment = "全部";

        /// <summary>右栏类别长列表（静态文案）。</summary>
        public ObservableCollection<string> SubCategories { get; } = new ObservableCollection<string>
        {
            "多人", "科技", "冒险", "魔法", "轻量", "任务", "水槽", "挑战", "优化", "战斗",
            "建造", "世界生成", "生物", "装备", "食物", "运输", "红石", "装饰"
        };

        /// <summary>按「包含内容」搜索框过滤后的类别列表。</summary>
        public ObservableCollection<string> FilteredSubCategories { get; } = new ObservableCollection<string>();

        public ObservableCollection<string> Environments { get; } = new ObservableCollection<string> { "全部", "客户端", "服务端" };

        /// <summary>「所有来源」下拉的选项（对应 SetSource(0/1)）。</summary>
        public ObservableCollection<string> SourceOptions { get; } = new ObservableCollection<string> { "所有来源 · Modrinth", "所有来源 · CurseForge" };

        /// <summary>列表视图的行数据（标签 / 更新时间 / 运行环境由详情按需补齐）。</summary>
        public ObservableCollection<ResourceRowItem> SearchRows { get; } = new ObservableCollection<ResourceRowItem>();

        /// <summary>列表视图数据源（带运行环境过滤）。</summary>
        public ICollectionView SearchRowsView { get; }

        /// <summary>分页条页码。</summary>
        public ObservableCollection<PageItem> PageItems { get; } = new ObservableCollection<PageItem>();

        public bool HasPrevPage => CurrentPage > 1;

        public string PageIndicator => $"第 {CurrentPage} / {TotalPages} 页";

        /// <summary>空状态：搜过但没有结果。</summary>
        public bool ShowEmptyState => IsSearchEmpty && !IsBusy && !HasSearchResults;

        /// <summary>精选列表：没有搜索结果、也没有落到空状态时才显示。</summary>
        public bool ShowFeatured => !HasSearchResults && !IsSearchEmpty;

        public bool HasActiveFilters => !string.IsNullOrEmpty(SelectedTag) || SelectedGameVersion != null || SelectedEnvironment != "全部";

        [RelayCommand]
        private void ToggleDisplayMode() => IsListView = !IsListView;

        [RelayCommand]
        private void ToggleCategoryList() => IsCategoryListExpanded = !IsCategoryListExpanded;

        [RelayCommand]
        private void SelectEnvironment(string? environment)
        {
            if (!string.IsNullOrEmpty(environment)) SelectedEnvironment = environment;
        }

        [RelayCommand]
        private void ClearFilters()
        {
            SelectedTag = null;
            SelectedGameVersion = null;
            SelectedEnvironment = "全部";
            ContentFilterText = string.Empty;

            if (IsFullPageView) RunSearch(true);
        }

        [RelayCommand]
        private void PrevPage()
        {
            if (CurrentPage <= 1 || !HasSearchResults) return;
            CurrentPage--;
            RunSearch(false);
        }

        [RelayCommand]
        private void NextPage()
        {
            if (!HasNextPage || !HasSearchResults) return;
            CurrentPage++;
            RunSearch(false);
        }

        [RelayCommand]
        private void GoToPage(int page)
        {
            if (page < 1 || page > TotalPages || page == CurrentPage) return;
            CurrentPage = page;
            RunSearch(false);
        }

        partial void OnSortIndexChanged(int value)
        {
            if (IsFullPageView)
            {
                RunSearch(true);
            }
            else
            {
                _featuredLoadCts?.Cancel();
                _featuredLoadCts = new CancellationTokenSource();
                _ = LoadFeaturedContentAsync(_featuredLoadCts.Token);
            }
        }

        partial void OnPageSizeChanged(int value)
        {
            if (IsFullPageView)
            {
                RunSearch(true);
                return;
            }
            RebuildPager();
        }

        partial void OnCurrentPageChanged(int value)
        {
            OnPropertyChanged(nameof(HasPrevPage));
            OnPropertyChanged(nameof(PageIndicator));
        }

        partial void OnTotalPagesChanged(int value) => OnPropertyChanged(nameof(PageIndicator));

        partial void OnIsSearchEmptyChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(ShowFeatured));
        }

        partial void OnHasSearchResultsChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(ShowFeatured));
        }

        partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(ShowEmptyState));

        partial void OnSelectedTagChanged(string? value) => OnPropertyChanged(nameof(HasActiveFilters));

        partial void OnSelectedGameVersionChanged(string? value) => OnPropertyChanged(nameof(HasActiveFilters));

        partial void OnSelectedEnvironmentChanged(string value)
        {
            OnPropertyChanged(nameof(HasActiveFilters));
            SearchRowsView?.Refresh();
        }

        partial void OnContentFilterTextChanged(string value) => RebuildFilteredSubCategories();

        /// <summary>搜索词 + 右栏子类别标签（详情级过滤走全文检索）。</summary>
        private string EffectiveQuery
        {
            get
            {
                var query = (SearchQuery ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(SelectedTag)) return query;
                return string.IsNullOrEmpty(query) ? SelectedTag! : query + " " + SelectedTag;
            }
        }

        /// <summary>统一的搜索入口（翻页 / 换排序 / 换数量都从这里走）。</summary>
        private void RunSearch(bool resetPage)
        {
            _searchDebounceCts?.Cancel();
            _searchDebounceCts = new CancellationTokenSource();
            _ = ExecuteSearchAsync(_searchDebounceCts.Token, resetPage);
        }

        private void ExitFullPageView()
        {
            IsFullPageView = false;
            var mainVM = Application.Current?.MainWindow?.DataContext as MainViewModel;
            if (mainVM != null) mainVM.IsGlobalResourcesOverlayActive = false;
        }

        private void RebuildFilteredSubCategories()
        {
            var keyword = (ContentFilterText ?? string.Empty).Trim();
            FilteredSubCategories.Clear();
            foreach (var tag in SubCategories)
            {
                if (keyword.Length == 0 || tag.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    FilteredSubCategories.Add(tag);
            }
        }

        /// <summary>按已加载条数估算总页数（至少覆盖当前页），并重建分页条。</summary>
        private void RebuildPager()
        {
            int pageSize = Math.Max(1, PageSize);
            int loaded = SearchResults.Count;
            int estimated = loaded == 0 ? 1 : (int)Math.Ceiling(loaded / (double)pageSize);

            TotalPages = Math.Max(1, Math.Max(CurrentPage, estimated));
            HasNextPage = loaded >= pageSize;

            PageItems.Clear();
            foreach (var item in BuildPageItems(CurrentPage, TotalPages)) PageItems.Add(item);

            OnPropertyChanged(nameof(PageIndicator));
            OnPropertyChanged(nameof(HasPrevPage));
        }

        private static List<PageItem> BuildPageItems(int current, int total)
        {
            var items = new List<PageItem>();

            if (total <= 7)
            {
                for (int i = 1; i <= total; i++) items.Add(MakePageItem(i, current));
                return items;
            }

            items.Add(MakePageItem(1, current));
            int start = Math.Max(2, current - 1);
            int end = Math.Min(total - 1, current + 1);

            if (start > 2) items.Add(new PageItem { Display = "…", IsEllipsis = true });
            for (int i = start; i <= end; i++) items.Add(MakePageItem(i, current));
            if (end < total - 1) items.Add(new PageItem { Display = "…", IsEllipsis = true });

            items.Add(MakePageItem(total, current));
            return items;
        }

        private static PageItem MakePageItem(int number, int current)
            => new PageItem { Number = number, Display = number.ToString(), IsCurrent = number == current };

        private bool MatchesEnvironment(ResourceRowItem row)
        {
            if (string.IsNullOrEmpty(SelectedEnvironment) || SelectedEnvironment == "全部") return true;
            if (!row.HasEnvironmentInfo) return true;   // 详情还没回来时先保留，避免误隐藏
            return SelectedEnvironment == "客户端" ? !row.IsServerSideOnly : !row.IsClientSideOnly;
        }

        /// <summary>鼠标悬停结果行时按需补齐详情（标签 / 更新时间 / 运行环境）。</summary>
        public void RequestRowEnrichment(ResourceRowItem? row)
        {
            if (row == null || row.IsEnriched || row.IsEnriching) return;
            _ = EnrichRowAsync(row);
        }

        private async Task EnrichRowAsync(ResourceRowItem row)
        {
            row.IsEnriching = true;
            try
            {
                await _enrichGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    // 复用详情页的预加载缓存：悬停过的行点进详情是秒开的
                    await PreloadDetailAsync(row.Project.Id, row.Project.Platform).ConfigureAwait(false);
                    var detail = await WaitForPreloadedDetailAsync(row.Project.Id).ConfigureAwait(false);
                    if (detail == null) return;

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        row.ApplyDetail(detail);
                        SearchRowsView?.Refresh();
                    });
                }
                finally { _enrichGate.Release(); }
            }
            catch { }
            finally { row.IsEnriching = false; }
        }

        /// <summary>0 = Modrinth，1 = CurseForge</summary>
        [ObservableProperty]
        private int _sourceIndex;

        [ObservableProperty]
        private bool _isCurseForgeActive;

        [ObservableProperty]
        private string _sourceNotice = string.Empty;

        /// <summary>切换内容源：重新加载热门 / 最新列表。</summary>
        public void SetSource(int index)
        {
            var platform = index == 1 ? Models.Ecosystem.ProjectPlatform.CurseForge : Models.Ecosystem.ProjectPlatform.Modrinth;
            _source.Platform = platform;
            IsCurseForgeActive = platform == Models.Ecosystem.ProjectPlatform.CurseForge;

            if (IsCurseForgeActive && !CurseForgeService.IsConfigured)
                SourceNotice = "CurseForge 需要 API Key：设置 → 下载 → CurseForge API Key（console.curseforge.com 免费申请）";
            else
                SourceNotice = "当前内容源：" + ContentSourceService.DisplayName(platform);

            _featuredLoadCts?.Cancel();
            _featuredLoadCts = new CancellationTokenSource();
            _ = LoadFeaturedContentAsync(_featuredLoadCts.Token);

            if (IsFullPageView) RunSearch(true);
        }

        [ObservableProperty]
        private bool _isFullPageView;

        [ObservableProperty]
        private bool _isSidebarCollapsed = true;

        /// <summary>自检 / 外部触发「收起 / 展开侧栏」—— 资源页侧的左栏折叠按钮原本只走 code-behind，
        /// 这里补一个 RelayCommand 让程序化测试也能走同一条路径。</summary>
        [RelayCommand]
        private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

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
            _source = new ContentSourceService();
            _modpackService = new ModpackService();
            _configService = ConfigService.Instance;

            SearchRowsView = CollectionViewSource.GetDefaultView(SearchRows);
            SearchRowsView.Filter = o => o is ResourceRowItem row && MatchesEnvironment(row);
            RebuildFilteredSubCategories();

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

                var trendingTask = _source.GetTrendingAsync(6, SelectedCategory, SelectedGameVersion);
                var newestTask = _source.GetNewestAsync(6, SelectedCategory, SelectedGameVersion);

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
            catch (OperationCanceledException)
            {
                // A newer load superseded us — make sure the spinner never stays on
                await Application.Current.Dispatcher.InvokeAsync(() => IsFeaturedLoading = false);
            }
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
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                mod.IconImage = img;
                                foreach (var row in SearchRows)
                                {
                                    if (ReferenceEquals(row.Project, mod)) row.NotifyIconChanged();
                                }
                            });
                        }
                    }
                });
                await Task.WhenAll(tasks);
            }
            catch { }
        }

        /// <summary>
        /// 内容类型（mod / modpack / resourcepack / datapack / shader）走 API 的 project_type；
        /// 右栏的中文子类别（多人 / 科技 / 冒险 …）只做标签过滤并折进搜索词 ——
        /// Modrinth / CurseForge 的 project_type 不接受中文子类别，不能直接丢给接口。
        /// </summary>
        [RelayCommand]
        private void SwitchCategory(string category)
        {
            if (string.IsNullOrEmpty(category)) return;

            if (SubCategories.Contains(category))
            {
                SelectedTag = SelectedTag == category ? null : category;   // 再点一次 = 取消该标签
                if (SelectedTag != null)
                {
                    RunSearch(true);
                    return;
                }
            }
            else
            {
                SelectedCategory = category;
                SelectedTag = null;
                SearchQuery = string.Empty;

                // ⭐ 如果当前已经在「展开全部」（整页浏览）里，**就地**换成新分类继续搜，
                //   不要回精选页 —— 否则用户明明点了「展开全部」，切个分类就把他弹回原样，
                //   「展开全部」按钮的意义就没了。
                if (IsFullPageView)
                {
                    RunSearch(true);
                    return;
                }

                ExitFullPageView();
            }

            HasSearchResults = false;
            _featuredLoadCts?.Cancel();
            _ = LoadFeaturedContentAsync();
        }

        [RelayCommand]
        private void FilterByVersion(string? version)
        {
            SelectedGameVersion = SelectedGameVersion == version ? null : version;

            // 已经在整页浏览 / 搜索里就地带版本重查，否则回精选
            if (IsFullPageView)
            {
                RunSearch(true);
                return;
            }

            SearchQuery = string.Empty;
            HasSearchResults = false;
            ExitFullPageView();
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

            await ExecuteSearchAsync(ct, true);
        }

        private async Task ExecuteSearchAsync(CancellationToken ct, bool resetPage = false, bool retryOnEmpty = true)
        {
            if (resetPage) CurrentPage = 1;

            IsFullPageView = true;
            var mainVM = Application.Current?.MainWindow?.DataContext as MainViewModel;
            if (mainVM != null) mainVM.IsGlobalResourcesOverlayActive = true;

            IsBusy = true;
            HasSearchResults = false;
            IsSearchEmpty = false;
            SearchResults.Clear();
            SearchRows.Clear();

            try
            {
                var query = EffectiveQuery;
                int pageSize = Math.Max(1, PageSize);
                int offset = (Math.Max(1, CurrentPage) - 1) * pageSize;

                var results = await _source.SearchProjectsAsync(query, pageSize, SortKey, SelectedCategory, offset, SelectedGameVersion).ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();

                if (results.Count == 0)
                {
                    // 翻过头了（结果数正好是每页条数的整数倍）：退回上一页重取
                    if (CurrentPage > 1 && retryOnEmpty)
                    {
                        CurrentPage--;
                        await ExecuteSearchAsync(ct, false, false).ConfigureAwait(false);
                        return;
                    }

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        IsSearchEmpty = true;
                        RebuildPager();
                    });
                    return;
                }

                await PreloadImagesAsync(results, ct);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var item in results)
                    {
                        SearchResults.Add(item);
                        SearchRows.Add(new ResourceRowItem(item));
                    }
                    HasSearchResults = true;
                    RebuildPager();
                });
            }
            catch (OperationCanceledException) { }
            catch (System.Exception ex)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsSearchEmpty = true;
                    RebuildPager();
                    if (ex is CurseForgeException)
                    {
                        SourceNotice = ex.Message;
                        iOS26Dialog.Show(ex.Message, "CurseForge", DialogIcon.Warning);
                    }
                    else
                    {
                        iOS26Dialog.Show($"搜索失败: {ex.Message}", "错误", DialogIcon.Error);
                    }
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
            SearchQuery = "";
            RunSearch(true);
        }

        [RelayCommand]
        private void GoBack()
        {
            IsFullPageView = false;
            var mainVM = Application.Current.MainWindow.DataContext as MainViewModel;
            if (mainVM != null) mainVM.IsGlobalResourcesOverlayActive = false;

            SearchQuery = "";
            SelectedTag = null;
            SearchResults.Clear();
            SearchRows.Clear();
            HasSearchResults = false;
            IsSearchEmpty = false;
            CurrentPage = 1;
            TotalPages = 1;
            HasNextPage = false;
            PageItems.Clear();
        }

        [RelayCommand]
        private async Task LoadMore()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                int pageSize = Math.Max(1, PageSize);
                int offset = (Math.Max(1, CurrentPage) - 1) * pageSize + SearchResults.Count;
                var query = EffectiveQuery;
                var results = await _source.SearchProjectsAsync(query, pageSize, SortKey, SelectedCategory, offset, SelectedGameVersion).ConfigureAwait(false);

                var imageTask = PreloadImagesAsync(results, CancellationToken.None);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var item in results)
                    {
                        SearchResults.Add(item);
                        SearchRows.Add(new ResourceRowItem(item));
                    }
                    RebuildPager();
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

                    // Make the new version visible in the launcher immediately
                    var mainVM = Application.Current.MainWindow.DataContext as MainViewModel;
                    if (mainVM != null) await mainVM.LoadVersionsAsync();

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

            // Let the user pick the target version (or cancel)
            var pickResult = ModInstallPicker.Show(
                Application.Current.MainWindow,
                mainVM?.GameVersions?.ToList() ?? new List<GameInstance>(),
                mainVM?.SelectedVersion,
                LocalMods.Count == 1 ? LocalMods[0].FileName : $"{LocalMods.Count} 个文件");

            if (pickResult.Cancelled || pickResult.Target == null) return;

            string versionId = pickResult.Target.Id;
            if (versionId.Contains(" "))
                versionId = versionId.Split(' ')[0];

            // Use the version's working directory so isolated versions get their
            // mods in the right place instead of the global .minecraft folder.
            string targetDir = pickResult.Target.GameDir;
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

            _ = PreloadDetailAsync(project.Id, project.Platform);

            var detailPage = new Views.ResourceDetailPage(project, gameVersion);
            NavigationService?.Navigate(detailPage);
        }

        public static async Task PreloadDetailAsync(string projectId, Models.Ecosystem.ProjectPlatform platform = Models.Ecosystem.ProjectPlatform.Modrinth)
        {
            if (_preloadCache.ContainsKey(projectId)) return;

            var service = new ContentSourceService { Platform = platform };
            _preloadCache.TryAdd(projectId, service.GetProjectDetailAsync(projectId, platform));
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