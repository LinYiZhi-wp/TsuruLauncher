using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TsuruLauncher.Controls;
using TsuruLauncher.Models;
using TsuruLauncher.Services;
using TsuruLauncher.Services.Animation;
using TsuruLauncher.Services.Ecosystem;
using TsuruLauncher.Services.Network;

namespace TsuruLauncher.ViewModels
{
    /// <summary>
    /// 「实例库 + 创建实例」页面的 ViewModel。
    /// 左侧（页面主体）是本地实例库：分段胶囊筛选 + 行卡片 + 空状态；
    /// 右侧（页面内嵌面板）是创建实例向导：图标 / 名称 / 游戏目录 / 游戏版本 / 加载器，
    /// 创建后切换成安装步骤条（进度来自 DownloadTask，0~1）。
    /// </summary>
    public partial class DownloadViewModel : ObservableObject
    {
        private readonly VersionManifestService _manifestService;
        private ObservableCollection<DownloadableVersion> _allVersions;

        // ───────────────────────── 版本清单（沿用原有属性/命令） ─────────────────────────

        [ObservableProperty]
        private bool _isLoading;

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _selectedFilter = "Release";

        // Download Sources
        public ObservableCollection<string> DownloadSources { get; } = new ObservableCollection<string>(VersionManifestService.AvailableSources);

        [ObservableProperty]
        private string _selectedSource = "BMCLAPI";

        public ICollectionView VersionsView { get; private set; }

        // ───────────────────────── 实例库 ─────────────────────────

        /// <summary>本地实例（含"刚创建、正在安装"的临时实例）。</summary>
        public ObservableCollection<InstanceCard> Instances { get; } = new ObservableCollection<InstanceCard>();

        /// <summary>按分段胶囊 + 搜索过滤后，真正显示在列表里的实例。</summary>
        public ObservableCollection<InstanceCard> InstancesView { get; } = new ObservableCollection<InstanceCard>();

        /// <summary>顶部实例筛选："All" / "Modpack" / "Server" / "Custom"。</summary>
        [ObservableProperty]
        private string _instanceFilter = "All";

        [ObservableProperty]
        private string _instanceSearchText = string.Empty;

        [ObservableProperty]
        private bool _hasInstances;

        [ObservableProperty]
        private bool _hasVisibleInstances;

        [ObservableProperty]
        private string _instanceCountText = "0 个实例";

        // ───────────────────────── 创建实例向导 ─────────────────────────

        [ObservableProperty]
        private bool _isWizardOpen;

        /// <summary>true = 向导已进入安装阶段（显示步骤条），false = 还在填表。</summary>
        [ObservableProperty]
        private bool _isInstallStage;

        [ObservableProperty]
        private string _instanceName = string.Empty;

        /// <summary>"Default"（默认目录）/ "Isolated"（版本隔离）/ "Shared"（版本共享）。</summary>
        [ObservableProperty]
        private string _directoryMode = "Default";

        /// <summary>向导里选中的游戏版本（版本下拉）。</summary>
        [ObservableProperty]
        private DownloadableVersion? _wizardVersion;

        /// <summary>向导里选中的加载器：None / Fabric / NeoForge / Forge / Quilt。</summary>
        [ObservableProperty]
        private string _wizardLoader = "None";

        [ObservableProperty]
        private ImageSource? _instanceIcon;

        [ObservableProperty]
        private string _instanceIconPath = string.Empty;

        [ObservableProperty]
        private bool _isInstalling;

        [ObservableProperty]
        private double _downloadProgress;

        [ObservableProperty]
        private string _installPercentText = "0%";

        [ObservableProperty]
        private string _installStatusText = "准备中…";

        /// <summary>当前安装阶段（准备 / 版本 JSON / 运行库 / 资源文件 / 加载器）。</summary>
        [ObservableProperty]
        private string _installStageText = "准备";

        // ── 创建结果反馈（非模态：面板内横幅 + 非阻塞 toast，绝不弹模态框） ──

        /// <summary>创建 / 安装成功。</summary>
        [ObservableProperty]
        private bool _createSucceeded;

        /// <summary>创建 / 安装失败（此时向导已经回滚到「填表」这一步，可以直接改完再点一次）。</summary>
        [ObservableProperty]
        private bool _createFailed;

        /// <summary>成功 / 失败的具体说明（成功 = 实例名 + 版本 + 加载器；失败 = 原因）。</summary>
        [ObservableProperty]
        private string _createResultText = string.Empty;

        public bool HasInstanceIcon => InstanceIcon != null;

        public bool CanCreateInstance => !string.IsNullOrWhiteSpace(InstanceName) && WizardVersion != null && !IsInstalling
                                         && !_createInFlight && !IsInstallStage;

        /// <summary>向导没开的时候「+ 创建实例」才可点（连点的第二下直接没有落点）。</summary>
        public bool CanOpenWizard => !IsWizardOpen;

        partial void OnIsWizardOpenChanged(bool value) => OnPropertyChanged(nameof(CanOpenWizard));


        // ── 游戏目录路径预览（三选一） ──

        public string DefaultGamePath
        {
            get
            {
                string path = ConfigService.Instance.Settings.GamePath;
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    path = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        ".minecraft");
                }
                return path;
            }
        }

        public string DefaultDirectoryPreview => DefaultGamePath;

        public string IsolatedDirectoryPreview =>
            Path.Combine(DefaultGamePath, "versions", string.IsNullOrWhiteSpace(InstanceName) ? "实例名" : SafeFolderName(InstanceName));

        public string SharedDirectoryPreview => DefaultGamePath;

        // ── 加载器可用性（不支持的项在界面上置灰 + ToolTip 说明） ──

        public bool IsNoneLoaderEnabled => true;

        public bool IsFabricLoaderEnabled => IsLoaderSupported(WizardVersion, "Fabric");

        public bool IsForgeLoaderEnabled => IsLoaderSupported(WizardVersion, "Forge");

        /// <summary>NeoForge 的安装器尚未接入，先置灰。</summary>
        public bool IsNeoForgeLoaderEnabled => false;

        /// <summary>Quilt 的安装器尚未接入，先置灰。</summary>
        public bool IsQuiltLoaderEnabled => false;

        public string FabricLoaderTip => IsFabricLoaderEnabled
            ? "Fabric 加载器（自动安装最新适配版本）"
            : "Fabric 需要 Minecraft 1.14 及以上版本";

        public string ForgeLoaderTip => IsForgeLoaderEnabled
            ? "Forge 加载器（自动安装推荐版本）"
            : "Forge 需要先选择一个游戏版本";

        public string NeoForgeLoaderTip => "NeoForge 安装器尚未接入，请先创建 Forge 实例，安装完成后再手动放入 NeoForge";

        public string QuiltLoaderTip => "Quilt 安装器尚未接入，可先创建 Fabric 实例（Quilt 兼容 Fabric 模组）";

        // ───────────────────────── 构造 ─────────────────────────

        public DownloadViewModel()
        {
            _manifestService = new VersionManifestService();
            _allVersions = new ObservableCollection<DownloadableVersion>();
            VersionsView = CollectionViewSource.GetDefaultView(_allVersions);
            VersionsView.Filter = FilterVersions;

            if (PreloadService.IsPreloaded && PreloadService.CachedVersionList.Count > 0)
            {
                foreach (var v in PreloadService.CachedVersionList)
                    _allVersions.Add(v);
                VersionsView.Refresh();
                IsLoading = false;
            }
            else
            {
                LoadVersionsCommand.Execute(null);
            }

            // 默认选中第一个正式版，方便向导直接点「创建实例」
            WizardVersion = _allVersions.FirstOrDefault(v => v.Type == "release") ?? _allVersions.FirstOrDefault();

            HookInstalledVersions();

            // 预热单例：DownloadManagerService 的构造函数会建 HttpClient / SocketsHttpHandler 与
            // 一个 DispatcherTimer（必须在 UI 线程上建），把它计进「创建实例」那一下的同步耗时里
            // 会直接顶掉 3ms 预算 —— 所以在这里（构造函数）先建好。
            try { _ = DownloadManagerService.Instance; } catch { }

            _ = RefreshInstances();
        }

        /// <summary>主界面扫描到新版本（例如安装完成）时，自动刷新实例库。</summary>
        private void HookInstalledVersions()
        {
            try
            {
                var mainVm = GetMainViewModel();
                if (mainVm == null) return;

                mainVm.GameVersions.CollectionChanged += (_, __) =>
                {
                    if (IsInstalling) return;
                    QueueInstanceRefresh();
                };
            }
            catch { }
        }

        /// <summary>
        /// 把「一次 LoadVersions（Clear + 逐条 Add）引发的 N 次集合变化」合并成**最多一次**刷新。
        ///
        /// 改前：每个 CollectionChanged 都 <c>BeginInvoke</c> 一次 <see cref="RefreshInstances"/>，
        /// 版本库有 N 个版本就是 N 次刷新（再加上 Clear 自己那一次），每次刷新都会在 UI 线程上
        /// 为每个已知实例做一遍磁盘 IO（<see cref="InstanceCard.FromInstance"/>）——
        /// 一次 LoadVersions 等于 N×M 次 UI 线程磁盘 IO，恰好落在「点完创建实例再点一次」的那一帧里。
        /// 改后：排一次就够了，多出来的变化直接丢掉（后一次刷新会把最新结果一起读进去）。
        /// </summary>
        private void QueueInstanceRefresh()
        {
            if (_instanceRefreshQueued) return;      // 已经排过一次：合并
            _instanceRefreshQueued = true;

            if (Application.Current?.Dispatcher != null)
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    _instanceRefreshQueued = false;
                    _ = RefreshInstances();
                }));
            }
            else
            {
                _instanceRefreshQueued = false;
                _ = RefreshInstances();
            }
        }

        private static MainViewModel? GetMainViewModel()
        {
            try
            {
                return Application.Current?.MainWindow?.DataContext as MainViewModel;
            }
            catch { return null; }
        }

        partial void OnSelectedSourceChanged(string value)
        {
            // Reload versions when source changes (if we were supporting dynamic switching of manifest source)
            // For now, VersionManifestService logic might need update or we just pass it
            LoadVersionsCommand.Execute(null);
        }

        [RelayCommand]
        private async Task LoadVersions()
        {
            IsLoading = true;
            try
            {
                // Pass source to service
                var versions = await _manifestService.GetVersionsAsync(SelectedSource);

                _allVersions.Clear();
                foreach (var v in versions)
                {
                    _allVersions.Add(v);
                }
                VersionsView.Refresh();

                if (WizardVersion == null)
                    WizardVersion = _allVersions.FirstOrDefault(v => v.Type == "release") ?? _allVersions.FirstOrDefault();
            }
            finally
            {
                IsLoading = false;
            }
        }

        partial void OnSearchTextChanged(string value)
        {
            VersionsView.Refresh();
        }

        partial void OnSelectedFilterChanged(string value)
        {
            VersionsView.Refresh();
            WizardVersion = VersionsView.Cast<DownloadableVersion>().FirstOrDefault() ?? WizardVersion;
        }

        private bool FilterVersions(object obj)
        {
            if (obj is not DownloadableVersion version) return false;

            // 1. Type Filter
            bool typeMatch = SelectedFilter switch
            {
                "Release" => version.Type == "release",
                "Snapshot" => version.Type == "snapshot" && !IsAprilFools(version.Id),
                "AprilFools" => version.Type == "snapshot" && IsAprilFools(version.Id),
                "Old" => version.Type.StartsWith("old_"),
                _ => true
            };

            if (!typeMatch) return false;

            // 2. Search Filter
            if (string.IsNullOrWhiteSpace(SearchText)) return true;

            return version.Id.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>愚人节版本在清单里同样是快照，按已知 ID 特征单独归类。</summary>
        private static bool IsAprilFools(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;

            string[] exact =
            {
                "15w14a", "1.rv-pre1", "3d shareware v1.34", "20w14infinite",
                "22w13oneblockatatime", "23w13a_or_b", "24w14potato", "25w14craftmine", "2.0"
            };
            string lower = id.ToLowerInvariant();
            if (exact.Contains(lower)) return true;

            string[] markers = { "oneblockatatime", "_or_b", "infinite", "shareware", "potato", "craftmine", "rv-pre1" };
            return markers.Any(m => lower.Contains(m));
        }

        [RelayCommand]
        private void SwitchFilter(string filter)
        {
            SelectedFilter = filter;
        }

        [RelayCommand]
        private void DownloadVersion(DownloadableVersion? version)
        {
            if (version == null) return;

            if (Application.Current?.MainWindow is MainWindow mw)
                mw.RootFrame.Navigate(new TsuruLauncher.Views.LoaderSelectionPage(version));
        }

        private string GetString(string key)
        {
            if (Application.Current.TryFindResource(key) is string s)
            {
                return s;
            }
            return $"[{key}]";
        }

        // ═════════════════════════ 实例库 ═════════════════════════

        /// <summary>实例分段胶囊：全部实例 / 整合包 / 服务器 / 自定义。</summary>
        [RelayCommand]
        private void SetInstanceFilter(string? filter)
        {
            InstanceFilter = string.IsNullOrWhiteSpace(filter) ? "All" : filter;
            ApplyInstanceFilter();
        }

        partial void OnInstanceFilterChanged(string value) => ApplyInstanceFilter();

        partial void OnInstanceSearchTextChanged(string value) => ApplyInstanceFilter();

        /// <summary>
        /// 刷新实例库。
        ///
        /// 改前：整条流程是同步 <c>void</c> —— <see cref="ScanLocalInstances"/> 会遍历
        /// <c>.minecraft/versions</c> 并逐个解析版本 JSON，全都在「点刷新 / 点创建实例」的
        /// 那一帧里跑完（版本越多、磁盘越慢越久）—— 这是「点一下就卡住」的第二个来源。
        /// 改后：扫描丢进 <see cref="Task.Run(Func{List{GameInstance}})" />，UI 线程只做
        /// 「把结果并进 ObservableCollection」这一件纯内存的小事（&lt; 1ms）。
        /// </summary>
        [RelayCommand]
        private async Task RefreshInstances()
        {
            int token = ++_instancesRefreshToken;
            int seq = ++_instancesRefreshSeq;
            string root = DefaultGamePath;

            var mainVm = GetMainViewModel();
            List<GameInstance>? installed = null;
            if (mainVm != null)
            {
                try { installed = mainVm.GameVersions.ToList(); } catch { installed = null; }
            }

            bool scanned = false;
            if (installed == null || installed.Count == 0)
            {
                var sw = Stopwatch.StartNew();
                installed = await Task.Run(() => ScanLocalInstances(root));
                scanned = true;
                // 注意：这是**线程池**上的耗时，不计入 3ms 的「同步阻塞」预算，
                // 所以用普通日志而不是 MotionPerf.Note（后者超预算会打 OVER-BUDGET，会被误读）。
                Utilities.Logger.LogInfo(
                    $"[MotionPerf] instances-scan thread=pool ms={sw.Elapsed.TotalMilliseconds:0.0} dir={root} count={installed.Count}");
            }

            // 卡片构建也要在线程池上：<see cref="InstanceCard.FromInstance"/> 每个实例有若干次
            // Directory.Exists / File.Exists / 目录时间戳查询。放在 UI 线程上时，「一次 LoadVersions
            // 触发 N 次刷新」就会变成 N×M 次 UI 线程磁盘 IO —— 这正是「第二次点击」被拖住的原因。
            var snapshot = installed ?? new List<GameInstance>();
            var pendingVersionIds = _pendingInstances
                .Select(p => p.VersionId)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToArray();

            var swBuild = Stopwatch.StartNew();
            var cards = await Task.Run(() => BuildInstanceCards(snapshot, pendingVersionIds, root));

            if (token != _instancesRefreshToken) return;   // 已被更新的一次刷新接管

            _knownInstances = snapshot;
            _knownCards = cards;
            var swUi = Stopwatch.StartNew();
            ApplyInstanceList();
            swUi.Stop();
            Utilities.Logger.LogInfo(
                $"[MotionPerf] instances-refresh seq={seq} token={token} known={_knownInstances.Count} " +
                $"build=pool {swBuild.Elapsed.TotalMilliseconds:0.0}ms uiMs={swUi.Elapsed.TotalMilliseconds:0.00} " +
                $"applyCalls={_applyListCalls} applyMs={_applyListTotalMs:0.00} scanned={scanned}");
        }

        /// <summary>线程池上把扫描结果转成卡片（内部全是磁盘 IO）。正在安装的版本让临时卡片代表。</summary>
        private static List<InstanceCard> BuildInstanceCards(List<GameInstance> games, string[] pendingVersionIds, string root)
        {
            var cards = new List<InstanceCard>(games.Count);
            foreach (var game in games)
            {
                if (pendingVersionIds.Any(id => string.Equals(id, game.Id, StringComparison.OrdinalIgnoreCase)))
                    continue;   // 这个版本正在安装：界面上由「准备中…」的临时卡片代表

                cards.Add(InstanceCard.FromInstance(game, root));
            }
            return cards;
        }

        /// <summary>
        /// 把「正在安装的临时实例 + 已知的已安装实例」并成界面列表 —— 纯内存操作，
        /// 可以直接在点击路径里同步调用（例如刚点下「创建实例」时先把「准备中…」的卡片放出来）。
        /// </summary>
        private void ApplyInstanceList()
        {
            var swApply = Stopwatch.StartNew();
            try
            {
                var collected = new List<InstanceCard>(_pendingInstances.Count + _knownCards.Count);

                // 1. 正在安装的临时实例（本地登记，安装完成后由扫描结果接管）
                collected.AddRange(_pendingInstances);

                // 2. 已安装实例的卡片：**已经在线程池上构建好了**，这里只做纯内存去重，
                //    一次磁盘 IO 都不做（原先这里对每个实例都重新 FromInstance 一遍）。
                foreach (var card in _knownCards)
                {
                    if (collected.Any(c => string.Equals(c.Id, card.Id, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    collected.Add(card);
                }

                Instances.Clear();
                foreach (var card in collected) Instances.Add(card);

                HasInstances = Instances.Count > 0;
                InstanceCountText = Instances.Count == 0 ? "0 个实例" : Instances.Count + " 个实例";
                ApplyInstanceFilter();
            }
            finally
            {
                _applyListCalls++;
                _applyListTotalMs += swApply.Elapsed.TotalMilliseconds;
            }
        }

        private static List<GameInstance> ScanLocalInstances(string root)
        {
            try
            {
                var service = new GameService(ConfigService.Instance);
                return service.ScanVersions(root, ConfigService.Instance.Settings.VersionIsolation);
            }
            catch
            {
                return new List<GameInstance>();
            }
        }

        private void ApplyInstanceFilter()
        {
            InstancesView.Clear();
            foreach (var card in Instances)
            {
                if (PassesInstanceFilter(card)) InstancesView.Add(card);
            }

            HasVisibleInstances = InstancesView.Count > 0;
        }

        /// <summary>当前分段胶囊 + 搜索词下，这张卡片该不该出现在列表里（纯内存判断）。</summary>
        private bool PassesInstanceFilter(InstanceCard card)
        {
            bool categoryOk = InstanceFilter switch
            {
                "Modpack" => card.Category == "Modpack",
                "Server" => card.Category == "Server",
                "Custom" => card.Category == "Custom",
                _ => true
            };
            if (!categoryOk) return false;

            if (!string.IsNullOrWhiteSpace(InstanceSearchText))
            {
                string key = InstanceSearchText.Trim();
                return (card.Name?.Contains(key, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (card.VersionId?.Contains(key, StringComparison.OrdinalIgnoreCase) ?? false) ||
                       (card.Loader?.Contains(key, StringComparison.OrdinalIgnoreCase) ?? false);
            }

            return true;
        }

        /// <summary>
        /// 「刚点下创建实例」用的**便宜**路径：只把这一张「准备中…」的卡片插到列表最前面。
        /// 不能走 <see cref="ApplyInstanceList"/> —— 那条路会对每个已知实例调用
        /// <see cref="InstanceCard.FromInstance"/>（内部有 Directory.Exists / File.Exists / 目录时间戳
        /// 等若干次磁盘 IO），而这里必须留在 &lt; 3ms 的同步预算内。
        /// </summary>
        private void InsertPendingCard(InstanceCard pending)
        {
            Instances.Insert(0, pending);
            if (PassesInstanceFilter(pending)) InstancesView.Insert(0, pending);

            HasInstances = Instances.Count > 0;
            HasVisibleInstances = InstancesView.Count > 0;
            InstanceCountText = Instances.Count == 0 ? "0 个实例" : Instances.Count + " 个实例";
        }

        /// <summary>行卡片的「开始游戏」：切到该实例并复用主界面的启动命令。</summary>
        [RelayCommand]
        private void PlayInstance(InstanceCard? card)
        {
            if (card == null) return;

            var mainVm = GetMainViewModel();
            if (mainVm == null) return;

            var target = card.Instance
                         ?? mainVm.GameVersions.FirstOrDefault(v => string.Equals(v.Id, card.VersionId, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                iOS26Dialog.Show($"实例「{card.Name}」的游戏文件还没装好。", "提示", DialogIcon.Warning);
                return;
            }

            mainVm.SelectedVersion = target;
            if (mainVm.LaunchGameCommand.CanExecute(null))
                mainVm.LaunchGameCommand.Execute(null);
        }

        /// <summary>行卡片的「安装」：沿用原有的「安装 → 选择加载器」流程。</summary>
        [RelayCommand]
        private void InstallInstance(InstanceCard? card)
        {
            if (card == null) return;

            var version = _allVersions.FirstOrDefault(v => string.Equals(v.Id, card.VersionId, StringComparison.OrdinalIgnoreCase))
                          ?? new DownloadableVersion { Id = card.VersionId };

            DownloadVersion(version);
        }

        // ═════════════════════════ 创建实例向导 ═════════════════════════

        [RelayCommand]
        private void OpenWizard()
        {
            // 连点幂等：向导已经开着就**不动用户已填的内容**。
            //   改前是无条件 ResetWizard()：连点的第二下会把用户刚填的名称 / 目录 / 加载器选择整段清空，
            //   紧接着点「创建实例」就会弹「请先给实例起个名字」的模态框（嵌套消息泵，看起来就是卡住）。
            //
            // [r4] 但「什么都不做」会把另一种残留变成死路：如果 IsWizardOpen 还是 true 而页面上的覆盖层
            //   因为上一轮动画被打断 / 页面重新加载停在了 Collapsed，按钮（CanOpenWizard=false）就是灰的，
            //   用户点「创建实例」完全没有落点 —— 看起来同样像"点了没反应"。所以这里改成**重播一次打开状态**：
            //   页面收到 PropertyChanged 后会走 ApplyWizardState(open, animate:false) 把覆盖层落回终态（自愈），
            //   但不会清空用户已经填好的名称 / 目录 / 加载器。
            var swOpen = Stopwatch.StartNew();
            if (IsWizardOpen)
            {
                RepairWizardResidue();
                OnPropertyChanged(nameof(IsWizardOpen));   // 状态重播：页面自愈到「打开」终态
                swOpen.Stop();
                MotionPerf.Note("wizard-open-dup", swOpen.Elapsed.TotalMilliseconds, "path=reassert-open");
                return;
            }

            // 上一次创建 / 安装留下的闩与阶段残留先清干净（可重入安全的前提）
            RepairWizardResidue();

            ResetWizard();

            // 重新打开时如果安装还在跑，直接回到「安装」阶段（下载不会因为关面板而停），
            // 而不是把用户丢回一个空的填表页 + 一个点不动的按钮。
            if (IsInstalling) IsInstallStage = true;

            IsWizardOpen = true;

            if (_allVersions.Count == 0 && !IsLoading)
                LoadVersionsCommand.Execute(null);

            swOpen.Stop();
            MotionPerf.Note("wizard-open-click", swOpen.Elapsed.TotalMilliseconds,
                $"reset=full versions={_allVersions.Count} loading={IsLoading} stage={IsInstallStage} inFlight={_createInFlight}");
        }

        /// <summary>「返回」：关闭向导回到实例库（下载仍会在下载管理器里继续）。</summary>
        [RelayCommand]
        private void CloseWizard()
        {
            IsWizardOpen = false;

            // [r4] 关闭时把「不复位就会残留到下一次」的状态清干净：
            //   * 没在装：_createInFlight（同步闩）与 IsInstallStage（安装步骤条）一起复位 ——
            //     否则下次打开向导看到的是上一次的安装界面、而且「创建实例」按钮因为闩没放开而永远点不动
            //     （用户报的"卡死"在状态机上就是这个样子）。
            //   * 正在装：保留安装阶段（下载在下载管理器里继续，重新打开还能看到进度），**绝不动闩**，
            //     否则第二次点击会在第一条链还在跑的时候再起一条。
            if (!IsInstalling)
            {
                bool latchWasSet = _createInFlight;
                _createInFlight = false;
                IsInstallStage = false;
                DownloadProgress = 0;
                InstallPercentText = "0%";
                InstallStageText = "准备";
                OnPropertyChanged(nameof(CanCreateInstance));
                CreateInstanceCommand.NotifyCanExecuteChanged();

                if (latchWasSet)
                    Utilities.Logger.LogInfo(
                        $"[MotionPerf] wizard-close residue-cleared latchWasSet=True installing=False " +
                        $"stage={IsInstallStage} pending={_pendingInstances.Count}");
            }
            else
            {
                Utilities.Logger.LogInfo(
                    $"[MotionPerf] wizard-close keep-install-stage installing=True stage={IsInstallStage} " +
                    $"pending={_pendingInstances.Count}");
            }
        }

        /// <summary>
        /// [r4] 清掉上一次创建 / 安装留下的状态残留 —— 向导「可重入安全」的关键一步。
        ///
        ///   * <c>_createInFlight</c> 是同步闩，正常由 <see cref="CreateInstance"/> 的 finally 复位；
        ///     一旦那条链因为异常 / 卡死而没有走到 finally，闩会**永远为真**，
        ///     <see cref="CanCreateInstance"/> 从此恒 false —— 界面上就是"按钮点不动、像卡住了"。
        ///     这里只在**确实没有安装在进行**时复位（IsInstalling=false，或者那条 task 已经跑完 / 失败）。
        ///   * <c>IsInstallStage</c>（安装步骤条）同理：没在装就必须回到「填表」阶段，
        ///     并且把上一次没走完的「准备中…」临时卡片从实例库里撤掉。
        /// 每次打开向导、以及「向导已经开着又点了一次」都会走这一步。
        /// </summary>
        private void RepairWizardResidue()
        {
            try
            {
                bool taskFinished = _currentTask == null || _currentTask.IsCompleted || _currentTask.IsFailed;
                bool reallyInstalling = IsInstalling && !taskFinished;

                if (_createInFlight && !reallyInstalling)
                {
                    _createInFlight = false;
                    Utilities.Logger.LogInfo(
                        $"[MotionPerf] wizard-residue repair latch inFlight=True installing={IsInstalling} " +
                        $"taskFinished={taskFinished} stage={IsInstallStage} pending={_pendingInstances.Count} -> cleared");
                }

                if (!reallyInstalling)
                {
                    if (IsInstallStage)
                    {
                        IsInstallStage = false;
                        Utilities.Logger.LogInfo("[MotionPerf] wizard-residue repair stage True -> False");
                    }

                    if (_pendingInstances.Count > 0)
                    {
                        foreach (var stuck in _pendingInstances)
                        {
                            stuck.IsInstalling = false;
                            if (string.IsNullOrEmpty(stuck.StatusText) || stuck.StatusText == "准备中…")
                                stuck.StatusText = "已取消";
                        }
                        _pendingInstances.Clear();
                        Utilities.Logger.LogInfo("[MotionPerf] wizard-residue repair pending-cards cleared");
                    }
                }

                OnPropertyChanged(nameof(CanCreateInstance));
                CreateInstanceCommand.NotifyCanExecuteChanged();
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "RepairWizardResidue");
            }
        }

        private void ResetWizard()
        {
            // 上一次的创建结果横幅不能带到这一次来（否则重新打开向导会看到过期的「创建失败」）
            CreateSucceeded = false;
            CreateFailed = false;
            CreateResultText = string.Empty;
            IsInstallStage = false;
            InstanceName = string.Empty;
            DirectoryMode = "Default";
            WizardLoader = "None";
            InstanceIcon = null;
            InstanceIconPath = string.Empty;
            DownloadProgress = 0;
            InstallPercentText = "0%";
            InstallStatusText = "准备中…";
            InstallStageText = "准备";
            WizardVersion = VersionsView.Cast<DownloadableVersion>().FirstOrDefault() ?? _allVersions.FirstOrDefault();
        }

        [RelayCommand]
        private void SetDirectoryMode(string? mode)
        {
            DirectoryMode = string.IsNullOrWhiteSpace(mode) ? "Default" : mode;
        }

        [RelayCommand]
        private void SelectLoader(string? loader)
        {
            string wanted = string.IsNullOrWhiteSpace(loader) ? "None" : loader;

            bool available = wanted switch
            {
                "Fabric" => IsFabricLoaderEnabled,
                "Forge" => IsForgeLoaderEnabled,
                "NeoForge" => IsNeoForgeLoaderEnabled,
                "Quilt" => IsQuiltLoaderEnabled,
                _ => true
            };

            if (!available) return;
            WizardLoader = wanted;
        }

        [RelayCommand]
        private void PickIcon()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "选择实例图标",
                Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 128;
                bitmap.UriSource = new Uri(dialog.FileName);
                bitmap.EndInit();
                bitmap.Freeze();

                InstanceIcon = bitmap;
                InstanceIconPath = dialog.FileName;
            }
            catch (Exception ex)
            {
                iOS26Dialog.Show($"图标读取失败：{ex.Message}", "提示", DialogIcon.Warning);
            }
        }

        [RelayCommand]
        private void RemoveIcon()
        {
            InstanceIcon = null;
            InstanceIconPath = string.Empty;
        }

        partial void OnInstanceIconChanged(ImageSource? value) => OnPropertyChanged(nameof(HasInstanceIcon));

        partial void OnInstanceNameChanged(string value)
        {
            OnPropertyChanged(nameof(IsolatedDirectoryPreview));
            OnPropertyChanged(nameof(CanCreateInstance));
        }

        partial void OnWizardVersionChanged(DownloadableVersion? value)
        {
            OnPropertyChanged(nameof(CanCreateInstance));
            OnPropertyChanged(nameof(IsFabricLoaderEnabled));
            OnPropertyChanged(nameof(IsForgeLoaderEnabled));
            OnPropertyChanged(nameof(IsNeoForgeLoaderEnabled));
            OnPropertyChanged(nameof(IsQuiltLoaderEnabled));
            OnPropertyChanged(nameof(FabricLoaderTip));
            OnPropertyChanged(nameof(ForgeLoaderTip));
            OnPropertyChanged(nameof(DirectoryModesHint));
        }

        partial void OnIsInstallingChanged(bool value)
        {
            OnPropertyChanged(nameof(CanCreateInstance));
            CreateInstanceCommand.NotifyCanExecuteChanged();
        }

        public string DirectoryModesHint => WizardVersion == null
            ? "先选择游戏版本，再决定文件放哪儿"
            : "版本 " + WizardVersion.Id + " 的文件会安装到这个目录";

        /// <summary>Fabric 支持 1.14+，Forge 需要先选版本。</summary>
        private static bool IsLoaderSupported(DownloadableVersion? version, string loader)
        {
            if (version == null) return false;

            if (loader == "Forge") return true;

            if (loader == "Fabric")
            {
                var parts = version.Id.Split('.');
                if (parts.Length < 2) return false;
                if (!int.TryParse(parts[0], out int major)) return false;
                if (major > 1) return true;
                if (!int.TryParse(parts[1], out int minor)) return false;
                return minor >= 14;
            }

            return false;
        }

        private static string SafeFolderName(string raw)
        {
            string name = raw;
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            name = name.Trim().TrimEnd('.');
            return string.IsNullOrEmpty(name) ? "实例名" : name;
        }

        // ── 创建实例 → 安装 ──

        private readonly List<InstanceCard> _pendingInstances = new List<InstanceCard>();

        /// <summary>最近一次扫描到的已安装实例（RefreshInstances 的异步结果缓存）。</summary>
        private List<GameInstance> _knownInstances = new List<GameInstance>();

        /// <summary>刷新轮次号：新的刷新发起后，旧的异步结果直接丢弃，不会把列表刷回去。</summary>
        private int _instancesRefreshToken;
        private int _instancesRefreshSeq;      // [MotionPerf] 埋点：第几次刷新
        private int _applyListCalls;           // [MotionPerf] 埋点：ApplyInstanceList 被调用了多少次
        private double _applyListTotalMs;      // [MotionPerf] 埋点：这些调用一共花了多少 UI 线程时间
        /// <summary>是否已经排了一次「实例库刷新」——把一次 LoadVersions 的 N 次集合变化合并成一次。</summary>
        private bool _instanceRefreshQueued;
        /// <summary>已经构建好的实例卡片（线程池产物），<see cref="ApplyInstanceList"/> 只做纯内存合并。</summary>
        private List<InstanceCard> _knownCards = new List<InstanceCard>();
        /// <summary>「创建实例」的同步闩：整条安装链在跑的时候，第二次点击直接忽略。</summary>
        private bool _createInFlight;
        private DownloadTask? _currentTask;
        private DispatcherTimer? _installTimer;

        [RelayCommand]
        private async Task CreateInstance()
        {
            // 连点幂等（第一道闸，必须在任何 await 之前）：
            //   _createInFlight 是同步闩，_createInFlight / IsInstalling / IsInstallStage 三者任一为真
            //   都说明这条链已经在跑或者已经跑完等用户确认了 —— 直接忽略，一次同步工作都不做。
            var swClick = Stopwatch.StartNew();
            bool busyAtEntry = _createInFlight || IsInstalling || IsInstallStage;
            if (busyAtEntry)
            {
                swClick.Stop();
                MotionPerf.Note("create-instance-click", swClick.Elapsed.TotalMilliseconds,
                    $"path=ignored-already-running inFlight={_createInFlight} installing={IsInstalling} " +
                    $"stage={IsInstallStage} pending={_pendingInstances.Count}");
                return;
            }
            if (string.IsNullOrWhiteSpace(InstanceName))
            {
                iOS26Dialog.Show("请先给实例起个名字。", "提示", DialogIcon.Info);
                return;
            }
            var selected = WizardVersion;
            if (selected == null)
            {
                iOS26Dialog.Show("请先选择游戏版本。", "提示", DialogIcon.Info);
                return;
            }

            string safeName = SafeFolderName(InstanceName);
            string root = DefaultGamePath;
            string gameDir = DirectoryMode switch
            {
                "Isolated" => Path.Combine(root, "versions", safeName),
                "Shared" => root,
                _ => root
            };

            string loaderChoice = WizardLoader switch
            {
                "Fabric" => "Fabric",
                "Forge" => "Forge",
                _ => "Vanilla"
            };
            string source = SelectedSource;

            var task = new DownloadTask
            {
                Name = safeName,
                Status = "正在准备安装…",
                VersionId = selected.Id,
                LoaderChoice = loaderChoice,
                Source = source
            };

            var pending = new InstanceCard
            {
                Id = safeName,
                Name = safeName,
                VersionId = selected.Id,
                Loader = loaderChoice == "Vanilla" ? "原版" : loaderChoice,
                Category = DirectoryMode == "Default" ? "Vanilla" : "Custom",
                GameDir = gameDir,
                Icon = InstanceIcon,
                StatusText = "准备中…",
                IsInstalling = true,
                Progress = 0
            };

            // ═══════════════════════════════════════════════════════════════════════
            //  「创建实例」点击 → 返回之间**唯一允许**的同步工作：
            //  若干字符串处理 + 属性赋值 + 往 ObservableCollection 里加两项。
            //  目录创建 / 加载器版本查询 / 版本 JSON 解析 / 资源索引解析 / 上千次
            //  File.Exists / 下载调度**全部**在下面第一个 await 之后（线程池）。
            //  实测这段同步耗时 < 1ms —— 见日志里的 [MotionPerf] create-instance sync=…
            // ═══════════════════════════════════════════════════════════════════════
            using (MotionPerf.Measure("create-instance",
                $"ver={selected.Id} loader={loaderChoice} mode={DirectoryMode} src={source}"))
            {
                _createInFlight = true;                           // 同步闩：整条链跑完才放开
                _currentTask = task;
                _pendingInstances.Add(pending);
                CreateSucceeded = false;
                CreateFailed = false;
                CreateResultText = string.Empty;
                IsInstallStage = true;
                IsInstalling = true;
                DownloadProgress = 0;
                InstallPercentText = "0%";
                InstallStageText = "准备";
                InstallStatusText = "正在准备安装…";

                InsertPendingCard(pending);                       // 纯内存 + 零磁盘 IO：先让「准备中…」的卡片出现
                DownloadManagerService.Instance.EnqueueTask(task); // 只是往 ObservableCollection 里加一项
                StartInstallTimer();
            }

            // 点击路径的同步阻塞：进入方法 -> 把安装链交给线程池为止（预算 3ms，见日志 create-instance-click）
            swClick.Stop();
            MotionPerf.Note("create-instance-click", swClick.Elapsed.TotalMilliseconds,
                $"path=enqueued busyAtEntry={busyAtEntry} ver={selected.Id} loader={loaderChoice} pending={_pendingInstances.Count}");

            string loaderVersion = string.Empty;
            string? failure = null;
            var bg = Stopwatch.StartNew();

            try
            {
                // ① 目录创建（磁盘）：线程池。
                await Task.Run(() =>
                {
                    Directory.CreateDirectory(gameDir);
                    if (DirectoryMode == "Isolated")
                        Directory.CreateDirectory(Path.Combine(gameDir, "mods"));
                });

                // ② 加载器适配版本查询（网络 + JSON）：本来就是 Task.Run，这里保持在线程池上。
                if (loaderChoice != "Vanilla")
                {
                    InstallStatusText = $"正在查找 {loaderChoice} 适配版本…";
                    loaderVersion = await ResolveLoaderVersionAsync(loaderChoice, selected.Id);

                    if (string.IsNullOrEmpty(loaderVersion))
                    {
                        failure = $"没有找到适配 Minecraft {selected.Id} 的 {loaderChoice} 版本，请改用其它加载器或换一个游戏版本。";
                    }
                    else
                    {
                        task.LoaderVersion = loaderVersion;
                    }
                }

                if (failure == null)
                {
                    // ③ 下载 + 安装：整条链路线程池化。
                    //
                    //    DownloadManagerService 里「版本 JSON 解析 / 资源索引（4MB，4000+ 条）解析 /
                    //    上千次 File.Exists / 目录创建」都发生在第一个 await 之后。只要调用点还带着
                    //    UI 的 SynchronizationContext，这些续体就会**回到 UI 线程**上跑（版本 JSON
                    //    已存在时甚至连一次挂起都没有，整段直接在点击的那一帧里跑完）——
                    //    这正是用户说的「点一下明显卡顿、像卡死」。Task.Run 让它们全部落到线程池。
                    string lv = loaderVersion;
                    await Task.Run(() => DownloadManagerService.Instance.EnqueueGameDownloadWithLoader(
                        task, selected, loaderChoice, lv, source));
                }
            }
            catch (Exception ex)
            {
                task.IsFailed = true;
                task.ErrorMessage = ex.Message;
                failure = ex.Message;
                Utilities.Logger.LogError(ex, "CreateInstance");
            }
            finally
            {
                StopInstallTimer();
                IsInstalling = false;

                if (failure == null && task.IsCompleted)
                {
                    DownloadProgress = 1.0;
                    InstallPercentText = "100%";
                    InstallStageText = "加载器";
                    InstallStatusText = "安装完成，实例已加入实例库";

                    pending.IsInstalling = false;
                    pending.Progress = 1.0;
                    pending.StatusText = "安装完成";
                    _pendingInstances.Remove(pending);

                    CreateFailed = false;
                    CreateSucceeded = true;
                    CreateResultText = $"「{safeName}」创建成功 · Minecraft {selected.Id}" +
                                       (loaderChoice == "Vanilla" ? " · 原版" : $" · {loaderChoice} {loaderVersion}");

                    Utilities.Logger.LogInfo(
                        $"[MotionPerf] create-instance-done elapsed={bg.Elapsed.TotalMilliseconds:0}ms " +
                        $"name={safeName} ver={selected.Id} loader={loaderChoice} " +
                        $"stage=ease-in-place motion=wizard-panel-from-form");
                    NotifyCreateResult(true, "创建成功", CreateResultText);

                    var mainVm = GetMainViewModel();
                    if (mainVm != null)
                    {
                        try { await mainVm.LoadVersionsAsync(); } catch { }
                    }

                    _ = RefreshInstances();
                }
                else
                {
                    string reason = failure
                                    ?? (string.IsNullOrWhiteSpace(task.ErrorMessage) ? task.Status : task.ErrorMessage)
                                    ?? "未知原因";
                    RollbackFailedCreate(pending, reason, bg.Elapsed.TotalMilliseconds);
                }

                _createInFlight = false;   // 整条链（含成功后的 LoadVersions / 刷新）结束，才允许下一次创建
                OnPropertyChanged(nameof(CanCreateInstance));   // 失败回滚后按钮要立刻重新可用
                CreateInstanceCommand.NotifyCanExecuteChanged();
            }
        }

        /// <summary>
        /// 创建 / 安装失败后的回滚：向导退回「填表」这一步（名字 / 版本 / 加载器都还在），
        /// 临时卡片撤掉，原因是**非模态**的横幅 + toast —— 界面必须回到「改完就能再点一次」的可用状态。
        /// </summary>
        private void RollbackFailedCreate(InstanceCard? pending, string reason, double elapsedMs)
        {
            try
            {
                if (pending != null)
                {
                    pending.IsInstalling = false;
                    pending.StatusText = "安装失败";
                    _pendingInstances.Remove(pending);
                }

                InstallPercentText = "0%";
                InstallStageText = "准备";
                InstallStatusText = "安装失败：" + reason;
                IsInstallStage = false;      // ← 回滚到可用状态：表单重新出现，可以直接再点一次
                CreateSucceeded = false;
                CreateFailed = true;
                CreateResultText = reason;

                ApplyInstanceList();

                Utilities.Logger.LogInfo(
                    $"[MotionPerf] create-instance-failed elapsed={elapsedMs:0}ms reason={reason} " +
                    "rollback=form-stage");

                NotifyCreateResult(false, "创建失败", reason);
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "RollbackFailedCreate");
            }
        }

        /// <summary>非模态提示（走主窗口的 toast，不启嵌套消息泵、绝不阻塞 Dispatcher）。</summary>
        private static void NotifyCreateResult(bool success, string title, string message)
        {
            try
            {
                var service = GetMainViewModel()?.NotificationService;
                if (service == null) return;
                if (success) service.ShowSuccess(title, message);
                else service.ShowError(title, message);
            }
            catch { }
        }

        private static async Task<string> ResolveLoaderVersionAsync(string loader, string mcVersion)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var api = new LoaderApiService();
                    var list = loader == "Fabric"
                        ? api.GetFabricVersions(mcVersion)
                        : api.GetForgeVersions(mcVersion);

                    if (list == null || list.Count == 0) return string.Empty;

                    var pick = list.FirstOrDefault(v => v.IsRecommended)
                               ?? list.FirstOrDefault(v => v.IsLatest)
                               ?? list[0];
                    return pick.Version ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            });
        }

        private void StartInstallTimer()
        {
            _installTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            _installTimer.Tick -= OnInstallTimerTick;
            _installTimer.Tick += OnInstallTimerTick;
            _installTimer.Start();
        }

        private void StopInstallTimer()
        {
            if (_installTimer == null) return;
            _installTimer.Stop();
            _installTimer.Tick -= OnInstallTimerTick;
        }

        private void OnInstallTimerTick(object? sender, EventArgs e)
        {
            var task = _currentTask;
            if (task == null) return;

            double progress = Math.Max(0, Math.Min(1, task.Progress));
            DownloadProgress = progress;
            InstallPercentText = ((int)Math.Round(progress * 100)) + "%";
            InstallStageText = ResolveStage(task);
            InstallStatusText = string.IsNullOrWhiteSpace(task.Status) ? "正在安装…" : task.Status;

            foreach (var card in _pendingInstances)
            {
                card.Progress = progress;
                card.StatusText = InstallStageText;
            }

            if (task.IsCompleted || task.IsFailed) StopInstallTimer();
        }

        /// <summary>把下载任务的子进度映射到「准备 → 版本 JSON → 运行库 → 资源文件 → 加载器」。</summary>
        private static string ResolveStage(DownloadTask task)
        {
            if (task.IsCompleted) return "加载器";
            if (task.ComponentsProgress > 0 || task.ComponentsStatus == "正在安装") return "加载器";
            if (task.AssetsProgress > 0 || task.AssetsStatus == "正在下载") return "资源文件";
            if (task.LibrariesProgress > 0 || task.LibrariesStatus == "正在下载") return "运行库";
            if (task.JsonProgress > 0 || task.JsonStatus == "正在下载") return "版本 JSON";
            return "准备";
        }

        // (ShowLoaderActionSheet removed — unused)
    }

    /// <summary>实例库里的一个行卡片（已安装实例，或刚创建、正在安装的实例）。</summary>
    public partial class InstanceCard : ObservableObject
    {
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        /// <summary>对应的 Minecraft 版本号（也是 versions 目录名）。</summary>
        public string VersionId { get; set; } = string.Empty;

        public string Loader { get; set; } = "原版";

        /// <summary>"Vanilla" / "Modpack" / "Server" / "Custom"。</summary>
        public string Category { get; set; } = "Vanilla";

        public string GameDir { get; set; } = string.Empty;

        public string LastPlayedText { get; set; } = "从未";

        [ObservableProperty]
        private ImageSource? _icon;

        [ObservableProperty]
        private bool _isInstalling;

        [ObservableProperty]
        private double _progress;

        [ObservableProperty]
        private string _statusText = string.Empty;

        /// <summary>已安装实例对应的模型；正在安装的临时实例为 null。</summary>
        public GameInstance? Instance { get; set; }

        public bool HasIcon => Icon != null;

        public bool HasGameDir => !string.IsNullOrWhiteSpace(GameDir);

        partial void OnIconChanged(ImageSource? value) => OnPropertyChanged(nameof(HasIcon));

        public static InstanceCard FromInstance(GameInstance game, string rootPath)
        {
            return new InstanceCard
            {
                Id = game.Id,
                Name = game.Id,
                VersionId = game.Id,
                Loader = DetectLoader(game.Id),
                Category = ClassifyInstance(game, rootPath),
                GameDir = game.GameDir ?? string.Empty,
                LastPlayedText = RelativeTime(game),
                Instance = game,
                IsInstalling = false,
                Progress = 1.0,
                StatusText = string.Empty
            };
        }

        /// <summary>加载器识别：完全按版本 ID 里的关键字（与主界面一致）。</summary>
        public static string DetectLoader(string id)
        {
            string lower = (id ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("neoforge")) return "NeoForge";
            if (lower.Contains("fabric")) return "Fabric";
            if (lower.Contains("quilt")) return "Quilt";
            if (lower.Contains("forge")) return "Forge";
            if (lower.Contains("optifine")) return "OptiFine";
            return "原版";
        }

        /// <summary>本地分类：整合包（含整合包清单文件）/ 服务器（含服务器配置）/ 自定义（独立游戏目录）/ 原版。</summary>
        public static string ClassifyInstance(GameInstance game, string rootPath)
        {
            string dir = game.GameDir ?? string.Empty;

            try
            {
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    if (File.Exists(Path.Combine(dir, "servers.dat")) ||
                        File.Exists(Path.Combine(dir, "server.properties")))
                        return "Server";

                    if (File.Exists(Path.Combine(dir, "modrinth.index.json")) ||
                        File.Exists(Path.Combine(dir, "manifest.json")) ||
                        File.Exists(Path.Combine(dir, "instance.cfg")))
                        return "Modpack";
                }
            }
            catch { }

            try
            {
                if (!string.IsNullOrEmpty(dir) && !string.IsNullOrEmpty(rootPath))
                {
                    string a = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
                    string b = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar);
                    if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return "Custom";
                }
            }
            catch { }

            return "Vanilla";
        }

        /// <summary>「最近游玩」用版本目录的最后写入时间做代理（存档 / 日志更新即视为玩过）。</summary>
        private static string RelativeTime(GameInstance game)
        {
            try
            {
                string versionDir = Path.Combine(game.RootPath ?? string.Empty, "versions", game.Id);
                string probe = Directory.Exists(versionDir) ? versionDir : game.GameDir;

                if (!string.IsNullOrEmpty(probe) && Directory.Exists(probe))
                {
                    var span = DateTime.Now - Directory.GetLastWriteTime(probe);
                    if (span.TotalMinutes < 1) return "刚刚";
                    if (span.TotalHours < 1) return (int)span.TotalMinutes + " 分钟前";
                    if (span.TotalDays < 1) return (int)span.TotalHours + " 小时前";
                    if (span.TotalDays < 30) return (int)span.TotalDays + " 天前";
                    return Directory.GetLastWriteTime(probe).ToString("yyyy-MM-dd");
                }
            }
            catch { }

            return "从未";
        }
    }
}
