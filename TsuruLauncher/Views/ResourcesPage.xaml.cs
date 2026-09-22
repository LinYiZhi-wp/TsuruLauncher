using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TsuruLauncher.Services.Animation;

namespace TsuruLauncher.Views
{
    public partial class ResourcesPage : Page
    {
        public ResourcesPage()
        {
            InitializeComponent();

            // 分类滑动指示块依赖「项已经排好版」的几何，宿主尺寸一变就重新对齐
            CategoryHost.SizeChanged += OnCategoryHostSizeChanged;

            Loaded += (_, __) =>
            {
                // 资源页默认展开左侧栏（折叠状态由用户自己切换）。
                // _sidebarReady = false 期间的同步一律不播动画（首帧直接落到展开态）。
                _sidebarReady = false;
                if (DataContext is ViewModels.ResourcesViewModel vm) vm.IsSidebarCollapsed = false;
                HookResourcesMotion();
                ApplySidebarState(false, animate: false);
                _sidebarReady = true;
                UpdateCategoryIndicator(false);

                // 网格-列表视图：首帧直接落到 VM 的当前状态，不播动画
                SyncViewVisibility(DataContext is ViewModels.ResourcesViewModel lvm && lvm.IsListView);
                ScheduleViewWarmUp("first-frame");
            };

            // 离开资源页（左导航 / 顶栏前进后退 / 任何入口）：复位整页浮层，
            // 让外壳的左导航栏与右侧信息面板恢复显示，并让资源页回到精选态。
            Unloaded += (_, __) =>
            {
                _sidebarReady = false;
                UnhookResourcesMotion();
                ForceSettleSidebar();
                ExitFullPageOverlay();
            };

            // TSURU_SELFTEST=resourcespage：把「两边收起 / 展开全部 / 切分类是否会退出展开」这一套
            // 全部按一次打日志 + 截图，方便定位 bug（用户的四个反馈都集中在这一页）。
            // TSURU_SELFTEST=collapsonly：只跑「收起 → 截图」，不污染其他步骤。
            string selfTest = Environment.GetEnvironmentVariable("TSURU_SELFTEST");
            if (selfTest == "resourcespage" || selfTest == "collapsonly")
                Loaded += async (_, __) => { await Task.Delay(2500).ConfigureAwait(true); Dispatcher.BeginInvoke(RunResourcesSelfTest, DispatcherPriority.Background); };
        }

        #region 内容切换动效（类别切换 / 搜索结果出现）

        private ViewModels.ResourcesViewModel? _motionVm;

        private System.Windows.Threading.DispatcherTimer? _refreshCoalesce;

        /// <summary>
        /// 结果是一次一条 / 一批一批加进来的，直接每次 CollectionChanged 都重播会闪；
        /// 用 120ms 的静默窗口把同一轮刷新合成一次动效。
        /// </summary>
        private const int RefreshCoalesceMs = 120;

        private void HookResourcesMotion()
        {
            if (DataContext is not ViewModels.ResourcesViewModel vm) return;
            if (ReferenceEquals(_motionVm, vm)) return;
            UnhookResourcesMotion();
            _motionVm = vm;
            vm.PropertyChanged += OnResourcesPropertyChanged;
            vm.SearchResults.CollectionChanged += OnResultsCollectionChanged;
            vm.SearchRows.CollectionChanged += OnResultsCollectionChanged;
        }

        private void UnhookResourcesMotion()
        {
            if (_motionVm == null) return;
            _motionVm.PropertyChanged -= OnResourcesPropertyChanged;
            _motionVm.SearchResults.CollectionChanged -= OnResultsCollectionChanged;
            _motionVm.SearchRows.CollectionChanged -= OnResultsCollectionChanged;
            _motionVm = null;
            _refreshCoalesce?.Stop();
            _refreshCoalesce = null;
            _viewWarmUp?.Stop();
            _viewWarmUp = null;
        }

        private void OnResultsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            ScheduleResultsRefresh("results");
            // 结果一落地就把「另一棵视图」排上预热：等用户点切换时容器已经在了
            ScheduleViewWarmUp("results");
        }

        private void OnResourcesPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(ViewModels.ResourcesViewModel.SelectedCategory):
                    // 旧结果先 120ms ease-in 淡出；新结果到了（集合变化 / IsBusy 落回 false）再淡入 + 错峰
                    using (MotionPerf.Measure("resources-category"))
                    {
                        PageTransition.PlayContentFadeOut(ResultsStack);
                    }
                    // 分类项的选中态 = 滑动指示块（150ms 单段 TranslateY），颜色过渡由
                    // NavItem 模板里的两层文字交叉淡入承担（各 150ms）。
                    UpdateCategoryIndicator(true);
                    Utilities.Logger.LogInfo(
                        $"[MotionPerf] resources category switch -> fade-out {AxolotlMotion.PageLeaveMs}ms ease-in " +
                        $"+ indicator {AxolotlMotion.NavSliderMs}ms translateY " +
                        $"(animations={PageTransition.AnimationsEnabled}) {MotionPerf.CacheSummary()}");
                    break;
                case nameof(ViewModels.ResourcesViewModel.IsSidebarCollapsed):
                    ApplySidebarState(
                        DataContext is ViewModels.ResourcesViewModel svm && svm.IsSidebarCollapsed,
                        animate: true);
                    break;
                case nameof(ViewModels.ResourcesViewModel.IsFullPageView):
                    // 整页视图（展开全部 / 返回精选）进出过渡：内容 180/220ms 淡入 + translateY ±30 -> 0 + 轻微 scale
                    PlayFullPageTransition(
                        DataContext is ViewModels.ResourcesViewModel fpv && fpv.IsFullPageView);
                    break;
                case nameof(ViewModels.ResourcesViewModel.IsBusy):
                    // 搜索结束的兜底：即使一条结果都没有（空状态），内容区也要淡回来
                    if (DataContext is ViewModels.ResourcesViewModel vm && !vm.IsBusy)
                        ScheduleResultsRefresh("search-done");
                    break;
                case nameof(ViewModels.ResourcesViewModel.HasSearchResults):
                    ScheduleResultsRefresh(e.PropertyName);
                    break;
                case nameof(ViewModels.ResourcesViewModel.IsFeaturedLoading):
                    // ⚠ 分类 / 游戏版本切换走的是「精选」这条链路（LoadFeaturedContentAsync），
                    // 它只把 IsFeaturedLoading 落回 false，**没有任何东西会把结果区淡回来**。
                    // 而上面 SelectedCategory 的 case 已经把 ResultsStack 淡出到 0 了 ——
                    // 于是切换分类后内容永远停在淡出态（用户看到的就是「点了分类什么都没有」）。
                    // 之前只监听了 HasSearchResults / IsBusy，这两个在精选链路上都不会变。
                    if (DataContext is ViewModels.ResourcesViewModel flvm && !flvm.IsFeaturedLoading)
                        ScheduleResultsRefresh("featured-loaded");
                    break;
                case nameof(ViewModels.ResourcesViewModel.IsListView):
                    // 网格 / 列表切换：走独立的「视图切换」路径（见 ApplyViewSwitch），
                    // 不再并进 120ms 合并窗口 —— 切换是用户直接点击的结果，不需要合并。
                    ApplyViewSwitch(
                        DataContext is ViewModels.ResourcesViewModel lvm && lvm.IsListView,
                        animate: true);
                    break;
            }
        }

        private void ScheduleResultsRefresh(string reason)
        {
            if (_refreshCoalesce == null)
            {
                _refreshCoalesce = new System.Windows.Threading.DispatcherTimer(
                    System.TimeSpan.FromMilliseconds(RefreshCoalesceMs),
                    System.Windows.Threading.DispatcherPriority.Background,
                    (_, __) =>
                    {
                        _refreshCoalesce?.Stop();
                        PlayResultsRefresh(reason);
                    },
                    Dispatcher);
            }
            _refreshCoalesce.Stop();
            _refreshCoalesce.Start();
        }

        /// <summary>
        /// 类别切换 / 搜索结果出现：内容区 180ms ease 淡入（page-enter 那一组），
        /// 列表逐项 28ms 步长 / 168ms 上限 / 180ms ease / translateY(-6px) 错峰
        ///（Axolotl Settings.vue:395 + 766-791）。
        /// </summary>
        private void PlayResultsRefresh(string reason)
        {
            if (ResultsStack == null) return;

            var itemHosts = new System.Collections.Generic.List<Panel>();
            using (MotionPerf.Measure("resources-refresh", "reason=" + reason))
            {
                PageTransition.PlayContentFadeIn(ResultsStack, staggerChildren: false);

                // 内容容器 220ms ease 淡入之后，卡片所在的「项宿主」面板（热门 / 最新 / 搜索结果网格 / 列表）
                // 逐个重播错峰入场：40ms 步长 / 200ms 上限 / 220ms ease / 起点 translateY(-8px)。
                // 只收「最外层」的项宿主（不再往已收集面板的子树里钻 —— 卡片里的标签 ItemsControl
                // 也是 WrapPanel，全收进来会让动画时钟数量翻好几倍），每个宿主错峰只作用于前 8 项。
                CollectItemHosts(ResultsStack, itemHosts);
                foreach (var host in itemHosts) StaggerFirstItems(host);
            }

            // 结果这一轮动效跑起来之后，把「另一棵视图」的容器预热好（问题 4）
            ScheduleViewWarmUp(reason);

            Utilities.Logger.LogInfo(
                $"[MotionPerf] resources content refresh ({reason}) fade {AxolotlMotion.PageEnterFadeMs}ms ease " +
                $"+ scale {AxolotlMotion.ContentSwitchFromScale}->1 / translateY {AxolotlMotion.ContentSwitchOffsetPx}px->0 " +
                $"over {AxolotlMotion.ContentSwitchMs}ms cubic-bezier(0.15,1.4,0.64,0.96) (was pure-fade 180ms, no move) " +
                $"+ stagger step={AxolotlMotion.StaggerStepMs}ms cap={AxolotlMotion.StaggerCapMs}ms " +
                $"item={AxolotlMotion.StaggerItemMs}ms translateY({AxolotlMotion.StaggerOffsetPx}px) " +
                $"panels={itemHosts.Count} (animations={PageTransition.AnimationsEnabled})");
        }

        /// <summary>
        /// 收集内容区里承载卡片的「项宿主」面板 ——
        /// ItemsControl 由 ItemsPanelTemplate 生成的那个 WrapPanel / StackPanel
        /// （TemplatedParent 是 ItemsPresenter），只给它们重播错峰，不会误伤普通布局面板。
        /// </summary>
        private static void CollectItemHosts(DependencyObject? root, System.Collections.Generic.List<Panel> into)
        {
            if (root == null) return;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is Panel panel &&
                    (panel is WrapPanel || panel.TemplatedParent is ItemsPresenter))
                {
                    // 命中的就是「承载卡片的项宿主」，到此为止：
                    // 卡片内部还有标签 ItemsControl（同样是 WrapPanel），
                    // 继续往下收只会把动画时钟数量翻几倍（每个宿主都再挂 8 个），收益为零。
                    into.Add(panel);
                    continue;
                }
                CollectItemHosts(child, into);
            }
        }

        #endregion

        #region 网格 / 列表视图切换（问题 4）

        /// <summary>
        /// 把两个视图的 Visibility 落到指定状态（联动 + 可被计时）。
        /// Visibility 不再走 XAML 绑定：绑定落地发生在 DataBind 优先级，
        /// 计时区间会跟它错开，而且没法在「点击这一帧」里把布局账拉进来一起量。
        /// </summary>
        private void SyncViewVisibility(bool list)
        {
            try
            {
                if (SearchGridResults != null) SearchGridResults.Visibility = list ? Visibility.Collapsed : Visibility.Visible;
                if (SearchListResults != null) SearchListResults.Visibility = list ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "SyncViewVisibility");
            }
        }

        /// <summary>
        /// 网格 / 列表切换（问题 4）。
        ///
        /// 改前实测（埋点原生数据，见 resources-viewswitch）：
        ///   切到列表 11 行 = <b>52.09ms</b> OVER-BUDGET；切到网格 20 张卡 = <b>29.24ms</b> OVER-BUDGET。
        /// 而且**再切一次就变成 ~1.3ms** —— 说明超预算的部分既不是 stagger（resources-refresh 只有
        ///  0.8~2.3ms，且已按 8 项封顶），也不是缩略图解码（图片是 ImageCache 预解码 + Freeze 过的 180px
        ///  位图，绑定不触发解码），而是「新视图那一整棵列表的容器生成 + 首帧布局」，
        ///  它以前正好落在点击这一帧。
        ///
        /// 现在的做法：
        ///   ① 结果到达后（Background 优先级、合并窗口之后）**预热**另一棵视图 ——
        ///      容器在那一刻就建好（resources-viewwarm 如实记账），点击时只剩 Visibility 对调；
        ///   ② 点击这一帧仍然强制一次 UpdateLayout 并如实记账 —— 预热没赶上（比如结果刚到就点）
        ///      也不会把代价藏起来，超预算照样打 OVER-BUDGET；
        ///   ③ 错峰只作用于前 <see cref="StaggerMaxItems"/> 项，其余直接到位。
        /// </summary>
        private void ApplyViewSwitch(bool list, bool animate)
        {
            try
            {
                int items = (DataContext as ViewModels.ResourcesViewModel)?.SearchResults.Count ?? 0;
                string detail = $"to={(list ? "list" : "grid")} items={items} animate={animate}";

                using (MotionPerf.Measure("resources-viewswitch", detail))
                {
                    SyncViewVisibility(list);

                    // 「点击这一帧」的真实代价必须算全：容器生成（若预热没完成）、绑定、排列
                    var entering = list ? (FrameworkElement?)SearchListResults : SearchGridResults;
                    entering?.UpdateLayout();

                    if (animate)
                    {
                        var host = FindItemsHost(entering);
                        StaggerFirstItems(host);
                    }
                }

                Utilities.Logger.LogInfo(
                    $"[MotionPerf] resources view-switch -> {(list ? "list" : "grid")} " +
                    $"stagger=first {StaggerMaxItems} only (step={AxolotlMotion.StaggerStepMs}ms " +
                    $"cap={AxolotlMotion.StaggerCapMs}ms item={AxolotlMotion.StaggerItemMs}ms " +
                    $"translateY({AxolotlMotion.StaggerOffsetPx}px)) warmUp={_viewWarmUpCount} " +
                    $"(animations={PageTransition.AnimationsEnabled}) {MotionPerf.CacheSummary()}");
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "ApplyViewSwitch");
            }
        }

        /// <summary>错峰只作用于前 8 项（超出的一律直接到位，不挂动画时钟）。</summary>
        private const int StaggerMaxItems = 8;

        private System.Windows.Threading.DispatcherTimer? _viewWarmUp;
        private int _viewWarmUpCount;

        /// <summary>
        /// 预热延迟：压在结果合并窗口（120ms）刚结束之后。
        /// 再晚就会出现「结果刚到、用户就点了切换」的窗口 —— 实测那条路径仍然要现场建容器
        /// （20 张卡 72.5ms OVER-BUDGET），预热得快一点才能把这个窗口压到最小。
        /// </summary>
        private const int ViewWarmUpDelayMs = 140;

        /// <summary>
        /// 排一次「预热另一棵视图」——放在 Background 优先级，等结果那一轮动效（120ms 合并 + 250ms 淡入）
        /// 先跑起来，再把另一棵视图的容器建出来。用户点「网格/列表」时就不会再现场建容器。
        /// </summary>
        private void ScheduleViewWarmUp(string reason)
        {
            try
            {
                if (_viewWarmUp == null)
                {
                    _viewWarmUp = new System.Windows.Threading.DispatcherTimer(
                        System.TimeSpan.FromMilliseconds(ViewWarmUpDelayMs),
                        System.Windows.Threading.DispatcherPriority.Background,
                        (_, __) =>
                        {
                            _viewWarmUp?.Stop();
                            WarmUpHiddenView();
                        },
                        Dispatcher);
                }
                _viewWarmUp.Stop();
                _viewWarmUp.Start();
            }
            catch { }
        }

        /// <summary>
        /// 预热「当前没显示的那一棵视图」：让它**真的被 Measure 一次**（Collapsed 的元素不会去量孩子），
        /// 容器就此生成并留在面板里，之后再切过去只是 Visibility 对调。
        ///
        /// 全程在同一个 Dispatcher 回调里同步完成 —— WPF 只在消息泵空闲时才渲染，
        /// 所以这次临时 Visible 不会被画出来（改完立刻还原，布局在下一帧按最终状态重算）。
        /// 代价如实记进 resources-viewwarm（这笔账是**从点击路径挪走的**，不是凭空消失的）。
        /// </summary>
        private void WarmUpHiddenView()
        {
            try
            {
                if (!IsLoaded) return;
                if (DataContext is not ViewModels.ResourcesViewModel vm) return;

                var hidden = vm.IsListView ? (FrameworkElement?)SearchGridResults : SearchListResults;
                if (hidden == null || hidden.Visibility == Visibility.Visible) return;
                if (vm.SearchResults.Count == 0) return;

                using (MotionPerf.Measure("resources-viewwarm",
                    $"hidden={(vm.IsListView ? "grid" : "list")} items={vm.SearchResults.Count}"))
                {
                    double height = hidden.Height;          // NaN = Auto
                    var visibility = hidden.Visibility;
                    hidden.Visibility = Visibility.Visible; // 只有 Visible 才会被 Measure（Collapsed 直接返回 0）
                    hidden.UpdateLayout();                  // 容器生成 + 完整一次布局
                    hidden.Height = height;
                    hidden.Visibility = visibility;
                    _viewWarmUpCount++;
                }
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "WarmUpHiddenView");
            }
        }

        /// <summary>
        /// 找 ItemsControl 里承载项的宿主面板（ItemsPresenter 下面的 WrapPanel / StackPanel）。
        /// 只做一次很浅的向下搜索（默认模板就是 ItemsControl → ItemsPresenter → Panel），
        /// 不做全树遍历 —— 全树 Walk 在几百张卡片时本身就要几毫秒。
        /// </summary>
        private static Panel? FindItemsHost(DependencyObject? root)
        {
            if (root == null) return null;

            var queue = new System.Collections.Generic.Queue<(DependencyObject node, int depth)>();
            queue.Enqueue((root, 0));
            while (queue.Count > 0)
            {
                var (node, depth) = queue.Dequeue();
                if (depth > 4) continue;

                int n = VisualTreeHelper.GetChildrenCount(node);
                for (int i = 0; i < n; i++)
                {
                    var child = VisualTreeHelper.GetChild(node, i);
                    if (child is Panel panel && (panel.TemplatedParent is ItemsPresenter || panel.IsItemsHost))
                        return panel;
                    queue.Enqueue((child, depth + 1));
                }
            }
            return null;
        }

        /// <summary>
        /// 只给**前 <see cref="StaggerMaxItems"/> 项**播错峰入场，其余一律直接到位。
        ///
        /// 与 <see cref="PageAnimation.ReplayStagger"/>（→ PageTransition.PlayStaggeredIn）的区别：
        ///   * 那里虽然也用 MaxStaggerItems 限流，但**每一项都要先 SettleElement 一遍**
        ///     （几十项 = 几百次 BeginAnimation(null)），这里是 O(8)；
        ///   * 错峰上限这里就是 8，符合「只作用于前 8 项」的要求。
        /// 只动 Opacity / RenderTransform（TranslateTransform.Y），结束强制定格并摘时钟。
        /// </summary>
        private static void StaggerFirstItems(Panel? host)
        {
            if (host == null) return;

            int count = Math.Min(StaggerMaxItems, host.Children.Count);
            for (int i = 0; i < count; i++)
            {
                if (host.Children[i] is not FrameworkElement child) continue;

                // 被上一轮打断时可能残留在 translateY(-8px) / 半透明：先归位再重播
                PageTransition.SettleElement(child);
                if (!PageTransition.AnimationsEnabled) continue;

                var translate = MotionVisuals.EnsureTranslate(child);
                child.BeginAnimation(UIElement.OpacityProperty, null);
                translate.BeginAnimation(TranslateTransform.YProperty, null);
                child.Opacity = 0;
                translate.Y = AxolotlMotion.StaggerOffsetPx;

                double delayMs = Math.Min(i * AxolotlMotion.StaggerStepMs, AxolotlMotion.StaggerCapMs);
                PageTransition.PlayOpacity(child, 1.0, AxolotlMotion.StaggerItemMs, AxolotlMotion.Ease, delayMs);

                // 位移：同一条 220ms ease，起点 -8px；结束时清时钟 + 落在 0（不留 HoldEnd 残留）
                var slide = new DoubleAnimation(AxolotlMotion.StaggerOffsetPx, 0,
                    AxolotlMotion.Ms(AxolotlMotion.StaggerItemMs))
                {
                    EasingFunction = AxolotlMotion.Ease,
                    BeginTime = TimeSpan.FromMilliseconds(delayMs),
                };
                slide.Completed += (_, __) =>
                {
                    translate.BeginAnimation(TranslateTransform.YProperty, null);
                    translate.Y = 0;
                };
                translate.BeginAnimation(TranslateTransform.YProperty, slide);

                // 兜底：Completed 没来也必须归位（与 PageTransition.SettleAfter 同一条约束）
                var guard = new System.Windows.Threading.DispatcherTimer(
                    System.TimeSpan.FromMilliseconds(AxolotlMotion.StaggerItemMs + delayMs + 80),
                    System.Windows.Threading.DispatcherPriority.Background,
                    (_, __) => { },
                    child.Dispatcher);
                guard.Tick += (s, e) =>
                {
                    guard.Stop();
                    try
                    {
                        translate.BeginAnimation(TranslateTransform.YProperty, null);
                        translate.Y = 0;
                    }
                    catch { }
                };
                guard.Start();
            }
        }

        #endregion

        #region 整页视图进出过渡（问题 3）

        /// <summary>
        /// 「展开全部」进入整页视图 / 「返回精选」退出：改前是完全硬切（实测同步 8.95ms / 17.50ms，
        /// 而且画面上没有任何过渡）。
        ///
        /// 现在进出都走 <see cref="PageTransition.PlayContentFadeIn"/> 那一套：
        ///   enter: opacity 0-&gt;1（220ms ease）+ translateY +30px-&gt;0
        ///          + scale 0.98-&gt;1（250ms cubic-bezier(0.15,1.4,0.64,0.96)）；
        ///   exit : 同一条动效**反向**（-30px，自上而下回来），因为内容本身还在，
        ///          直接重播一次进场就是最自然的「反向」。
        /// 只动 Opacity / RenderTransform；根 Grid 超过 1.2MP 上限，BitmapCache 会被跳过（不会挂整页大缓存）。
        /// </summary>
        private void PlayFullPageTransition(bool entering)
        {
            if (ResourcesRoot == null) return;

            using (MotionPerf.Measure("resources-fullpage",
                $"fullPage={entering} dir={(entering ? "+" : "-")}{AxolotlMotion.SlideOffsetPx}px " +
                $"fade={AxolotlMotion.PageEnterFadeMs}ms ease move/scale={AxolotlMotion.ContentSwitchMs}ms " +
                "cubic-bezier(0.15,1.4,0.64,0.96)"))
            {
                PageTransition.PlayContentFadeIn(
                    ResourcesRoot,
                    staggerChildren: false,
                    offsetY: entering ? AxolotlMotion.SlideOffsetPx : -AxolotlMotion.SlideOffsetPx,
                    fromScale: 0.98);
            }

            Utilities.Logger.LogInfo(
                $"[MotionPerf] resources full-page {(entering ? "enter" : "back")} " +
                $"fade {AxolotlMotion.PageEnterFadeMs}ms ease + translateY {(entering ? "+" : "-")}{AxolotlMotion.SlideOffsetPx}px->0 " +
                $"+ scale 0.98->1 over {AxolotlMotion.ContentSwitchMs}ms cubic-bezier(0.15,1.4,0.64,0.96) " +
                $"(was: Visibility 硬切 / 无过渡) (animations={PageTransition.AnimationsEnabled})");
        }

        #endregion

        #region 类别滑动指示块（问题 2）

        private double _catIndicatorY = double.NaN;
        private double _catIndicatorH = double.NaN;
        private double _catIndicatorW = double.NaN;
        private double _catIndicatorX = double.NaN;
        private bool _catIndicatorReady;

        /// <summary>指示块正在播位移动画（宿主 SizeChanged 的被动刷新不要打断它）。</summary>
        private bool _catIndicatorAnimating;

        /// <summary>
        /// 侧栏宽度动画（收起 / 展开）正在播。
        /// 这期间 CategoryHost 会因为两套列表换形而触发 SizeChanged：
        /// 收起态项高 40、展开态项高 ~31，指示块的 Y 一定会变；
        /// 但此刻两套列表还在交叉淡入淡出，让指示块当场瞬移过去非常跳。
        /// 所以宽度动画期间**只跟随宽度**，位移留到动画收尾的 onCompleted 里用
        /// 同一个 150ms translateY 滑过去（Axolotl NavRail 的同一套机制）。
        /// </summary>
        private bool _sidebarWidthAnimating;

        /// <summary>展开 / 折叠两套列表里「当前可见的那一套」的选中项（几何完全对齐）。</summary>
        private RadioButton? SelectCategoryButton()
        {
            RadioButton? fallback = null;
            foreach (var host in new[] { SidebarExpanded, SidebarCollapsed })
            {
                if (host == null || host.Visibility != Visibility.Visible) continue;
                foreach (var child in host.Children)
                {
                    if (child is RadioButton rb && rb.IsChecked == true)
                    {
                        if (rb.ActualWidth >= 1 && rb.ActualHeight >= 1) return rb;
                        fallback ??= rb;
                    }
                }
            }
            return fallback;
        }

        private void OnCategoryHostSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 侧栏宽度动画期间：两套列表在换形，位移留到 onCompleted 统一收口
            if (_sidebarWidthAnimating) { SyncIndicatorWidthOnly(); return; }
            if (!_catIndicatorReady) { UpdateCategoryIndicator(false); return; }
            SyncIndicatorGeometry();
        }

        /// <summary>宽度变了而位移正在播：只跟宽度，绝不把正在跑的位移拽回去（NavSlider 踩过的坑）。</summary>
        private void SyncIndicatorWidthOnly()
        {
            try
            {
                if (CategoryIndicator == null) return;
                var selected = SelectCategoryButton();
                if (selected == null || selected.ActualWidth < 1) return;

                double w = selected.ActualWidth;
                if (double.IsNaN(CategoryIndicator.Width) || Math.Abs(CategoryIndicator.Width - w) > 0.5)
                {
                    CategoryIndicator.Width = w;
                    _catIndicatorW = w;
                }
            }
            catch { }
        }

        private void SyncIndicatorGeometry()
        {
            if (_catIndicatorReady && _catIndicatorAnimating) { SyncIndicatorWidthOnly(); return; }
            UpdateCategoryIndicator(false);
        }

        private void UpdateCategoryIndicator(bool animate)
        {
            if (animate)
            {
                using (MotionPerf.Measure("resources-nav-indicator")) UpdateCategoryIndicatorCore(true);
            }
            else
            {
                UpdateCategoryIndicatorCore(false);
            }
        }

        /// <summary>
        /// 滑动指示块（Axolotl NavRail.vue:104-115，与 MainWindow.UpdateNavSlider 同一套机制）：
        ///   * 位置只由 <b>一个 TranslateY</b> 承担：Margin.Top 立刻写到新格子，位移交给变换；
        ///   * 变换起点 = 当前动画值 + (上一次目标 - 新目标)，所以连点 / 反向点都不跳；
        ///   * 高度用 Height 承载（不参与动画 —— 动画 Height 会改宿主 desired size 并触发
        ///     自己打断自己的反馈环，这正是 NavSlider 当初抖动的原因）；
        ///   * 两个方向同一条 150ms cubic-bezier(0.4, 0, 0.2, 1)，没有 BeginTime 错峰、没有 ScaleY 补偿；
        ///   * 结束摘时钟并把位移落到 0，不留 HoldEnd 残留。
        /// 只动 RenderTransform / Height / Width，不触摸任何会触发布局循环的动画属性。
        /// </summary>
        private void UpdateCategoryIndicatorCore(bool animate)
        {
            try
            {
                if (CategoryIndicator == null || CategoryHost == null || CategoryIndicatorTranslate == null) return;

                var selected = SelectCategoryButton();
                if (selected == null || selected.ActualWidth < 1 || selected.ActualHeight < 1) return;

                var origin = selected.TransformToAncestor(CategoryHost).Transform(new Point(0, 0));
                double w = selected.ActualWidth;
                double h = selected.ActualHeight;
                double newY = origin.Y;
                // 收起态：项只有 40px 宽、在 46px 内容区里居中（左右各 3px）。
                // 指示块的 X 必须跟着走，否则就会出现截图里那种「块和图标没对齐」。
                double newX = origin.X;

                bool first = double.IsNaN(_catIndicatorY) || !_catIndicatorReady;
                double prevY = first ? newY : _catIndicatorY;
                double prevH = (double.IsNaN(_catIndicatorH) || first) ? h : _catIndicatorH;
                double prevW = (double.IsNaN(_catIndicatorW) || first) ? w : _catIndicatorW;
                double prevX = (double.IsNaN(_catIndicatorX) || first) ? newX : _catIndicatorX;

                // 被动的布局刷新：目标没变就什么都别做（尤其别打断正在跑的动画）
                if (!animate && !first &&
                    Math.Abs(prevY - newY) < 0.5 && Math.Abs(prevH - h) < 0.5 &&
                    Math.Abs(prevW - w) < 0.5 && Math.Abs(prevX - newX) < 0.5)
                {
                    return;
                }

                // 先读出「动画中的当前值」，再清时钟 —— 清完读到的就是基值了
                double animatedOffsetY = CategoryIndicatorTranslate.Y;

                CategoryIndicatorTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                CategoryIndicator.BeginAnimation(FrameworkElement.HeightProperty, null);

                CategoryIndicator.Width = w;
                CategoryIndicator.Margin = new Thickness(newX, newY, 0, 0);
                CategoryIndicator.Height = h;
                _catIndicatorY = newY;
                _catIndicatorH = h;
                _catIndicatorW = w;
                _catIndicatorX = newX;
                _catIndicatorReady = true;

                // 眼下指示块顶端在哪：上一次目标位置 + 当前动画偏移
                double visualTop = prevY + (double.IsNaN(animatedOffsetY) ? 0.0 : animatedOffsetY);
                double fromY = visualTop - newY;

                bool skipMotion = !animate || !PageTransition.AnimationsEnabled || Math.Abs(fromY) < 0.5;

                if (skipMotion)
                {
                    CategoryIndicatorTranslate.Y = 0;
                    _catIndicatorAnimating = false;

                    if (first)
                    {
                        // 首次出现：opacity 250ms cubic-bezier(0.5, 0, 0.2, 1) 延迟 50ms（同 NavSlider）
                        var fade = new DoubleAnimation(0, 1, AxolotlMotion.Ms(AxolotlMotion.NavSliderFadeMs))
                        {
                            EasingFunction = AxolotlMotion.NavSliderFadeEase,
                            BeginTime = TimeSpan.FromMilliseconds(AxolotlMotion.NavSliderFadeDelayMs),
                        };
                        CategoryIndicator.BeginAnimation(UIElement.OpacityProperty, fade);
                    }
                    else
                    {
                        CategoryIndicator.BeginAnimation(UIElement.OpacityProperty, null);
                        CategoryIndicator.Opacity = 1;
                    }
                    return;
                }

                CategoryIndicatorTranslate.Y = fromY;
                _catIndicatorAnimating = true;

                var move = new DoubleAnimation(fromY, 0, AxolotlMotion.Ms(AxolotlMotion.NavSliderMs))
                {
                    EasingFunction = AxolotlMotion.EaseInOut,
                };
                move.Completed += (s, e) =>
                {
                    CategoryIndicatorTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                    CategoryIndicatorTranslate.Y = 0;      // 结束归位，不留 HoldEnd 残留
                    _catIndicatorAnimating = false;
                };
                CategoryIndicatorTranslate.BeginAnimation(TranslateTransform.YProperty, move);

                Utilities.Logger.LogInfo(
                    $"[MotionPerf] resources category indicator {(newY > prevY ? "down" : "up")} " +
                    $"delta={newY - prevY:+0.#;-0.#}px from={fromY:+0.#;-0.#}px->0 " +
                    $"{AxolotlMotion.NavSliderMs}ms cubic-bezier(0.4,0,0.2,1) height={h:0.#} width={w:0.#} x={newX:0.#} " +
                    $"stagger=0 scaleY=1 (was: 每项自己换底色 = 硬切) {MotionPerf.CacheSummary()}");
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "UpdateCategoryIndicator");
            }
        }

        #endregion

        #region 左侧栏折叠 / 展开（问题 3）

        private const double SidebarExpandedWidth = 212;
        private const double SidebarCollapsedWidth = 64;

        /// <summary>首帧 / 离场同步不播动画（Loaded、Unloaded 直接落终态）。</summary>
        private bool _sidebarReady;

        /// <summary>
        /// 收起 / 展开时「必须彻底不显示」的文字容器：
        /// 标题、展开态整块列表、底部按钮。收起 = 150ms 淡出 + 动画结束后 Visibility=Collapsed；
        /// 展开 = 立刻可见（opacity 0）+ 延迟 80ms 在宽度动画后段淡入。
        /// </summary>
        private FrameworkElement[] SidebarTextTargets()
            => new FrameworkElement[] { SidebarTitle, SidebarExpanded, SidebarFooter };

        private void ApplySidebarState(bool collapsed, bool animate)
        {
            if (SidebarCard == null) return;

            bool motion = animate && _sidebarReady && IsLoaded && PageTransition.AnimationsEnabled;

            // ① 宽度：200ms cubic-bezier(0.22, 1, 0.36, 1)（Axolotl --right-bar-width 同曲线）；
            //    动画期给卡片挂 BitmapCache（受 MotionAssist.MaxCacheDevicePixels = 1.2MP 上限约束），
            //    结束强制定格 + 摘缓存。只动 Width，不碰 Margin。
            _sidebarWidthAnimating = motion;
            PageTransition.AnimateElementWidth(
                SidebarCard,
                collapsed ? SidebarCollapsedWidth : SidebarExpandedWidth,
                AxolotlMotion.SidebarCollapseMs,
                AxolotlMotion.SidebarCollapseEase,
                () =>
                {
                    _sidebarWidthAnimating = false;
                    SettleSidebarText(collapsed);
                    // 收起态项高 40 / 展开态项高 ~31，Y 与 X 都会变：
                    // 让指示块用同一条 150ms translateY 滑过去，而不是当场瞬移。
                    UpdateCategoryIndicator(motion);
                });

            using (MotionPerf.Measure("resources-sidebar",
                $"collapsed={collapsed} animate={motion} " +
                $"width={(collapsed ? SidebarCollapsedWidth : SidebarExpandedWidth)} " +
                $"{AxolotlMotion.SidebarCollapseMs}ms cubic-bezier(0.22,1,0.36,1) " +
                $"textOpacity={AxolotlMotion.SidebarTextFadeMs}ms delay={(collapsed ? 0 : AxolotlMotion.SidebarTextFadeInDelayMs)}ms " +
                $"collapseAfterFade={(collapsed ? "Visibility=Collapsed" : "Visibility=Visible")}"))
            {
                if (collapsed)
                {
                    // 图标列表立刻顶上（与展开态同网格同模板，几何完全重合，不会跳），
                    // 文字整块 150ms 淡出，淡出结束（动画之后）才 Visibility=Collapsed。
                    SidebarCollapsed.Visibility = Visibility.Visible;
                    FadeSidebarTexts(0, 0, () => SetSidebarTextVisibility(Visibility.Collapsed), motion);
                }
                else
                {
                    // 展开时「图标列表」不要立刻隐藏：展开态整块是 opacity 0 起步的，
                    // 立刻隐藏折叠态会让侧栏在 80ms 淡入开始前出现一段「空的」空窗；
                    // 两块同格叠着，等动画收尾（SettleSidebarText）再切掉折叠态。
                    SetSidebarTextVisibility(Visibility.Visible);
                    FadeSidebarTexts(1, AxolotlMotion.SidebarTextFadeInDelayMs, null, motion);
                }
            }
        }

        /// <summary>
        /// 文字淡入 / 淡出。展开方向先把基值写成 0（再交给带 BeginTime 的淡入），
        /// 保证延迟的 80ms 里文字确实是不可见的 —— 绝不会「一开始就被宽度动画挤压变形」。
        /// </summary>
        private void FadeSidebarTexts(double to, double delayMs, Action? onCompleted, bool animate)
        {
            var targets = SidebarTextTargets();

            if (!animate)
            {
                foreach (var el in targets) PageTransition.SettleOpacity(el, to);
                onCompleted?.Invoke();
                return;
            }

            if (to >= 1.0)
            {
                foreach (var el in targets) PageTransition.SettleOpacity(el, 0.0);
            }

            int remaining = targets.Length;
            foreach (var el in targets)
            {
                PageTransition.PlayOpacity(el, to, AxolotlMotion.SidebarTextFadeMs, AxolotlMotion.EaseInOut, delayMs,
                    () => { if (--remaining <= 0) onCompleted?.Invoke(); });
            }
        }

        private void SetSidebarTextVisibility(Visibility visibility)
        {
            foreach (var el in SidebarTextTargets())
            {
                if (el != null) el.Visibility = visibility;
            }
        }

        /// <summary>把侧栏文字强制定格到「折叠 = 完全不可见 / 展开 = 完全可见」。</summary>
        private void SettleSidebarText(bool collapsed)
        {
            try
            {
                foreach (var el in SidebarTextTargets())
                    PageTransition.SettleOpacity(el, collapsed ? 0.0 : 1.0);

                SetSidebarTextVisibility(collapsed ? Visibility.Collapsed : Visibility.Visible);
                SidebarCollapsed.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
            }
            catch { }
        }

        /// <summary>离场 / 首帧：取消所有在飞的侧栏动画并直接落到当前状态的终态。</summary>
        private void ForceSettleSidebar()
        {
            try
            {
                bool collapsed = DataContext is ViewModels.ResourcesViewModel vm && vm.IsSidebarCollapsed;

                _sidebarWidthAnimating = false;
                if (SidebarCard != null)
                {
                    SidebarCard.BeginAnimation(FrameworkElement.WidthProperty, null);
                    SidebarCard.Width = collapsed ? SidebarCollapsedWidth : SidebarExpandedWidth;
                    MotionAssist.EnableBitmapCache(SidebarCard, false);
                }

                SettleSidebarText(collapsed);
            }
            catch { }
        }

        #endregion

        #region 滚轮路由（问题 1）

        private readonly List<ScrollViewer> _wheelChain = new List<ScrollViewer>();

        private double _lastScrollLoggedOffset = double.NaN;

        /// <summary>
        /// 滚轮路由 —— 替换掉旧的「<c>e.Handled = true</c> 再把事件重抛给 <c>Parent</c>」写法。
        ///
        /// 旧写法为什么把整个滚动吃掉了：主内容区 ScrollViewer 自己挂着 PreviewMouseWheel，
        /// 一进来就 <c>Handled = true</c> 并往 <c>ScrollViewer.Parent</c>（一个普通 Grid）重抛 ——
        /// 重抛出来的事件是**从外层重新往下隧行**的，主 ScrollViewer 自己不在那条路由上，
        /// 于是谁都没滚；指针在左栏 / 右栏时同理。
        ///
        /// 现在的规则（用 VerticalOffset / ScrollableHeight 判边界，不再重抛）：
        ///   1. 指针下「最靠内、且还能朝这个方向滚」的 ScrollViewer 自己滚 —— 直接放行原生处理
        ///      （原生步长 / 原生惯性），所以主内容区的滚轮手感和系统完全一致；
        ///   2. 该面板已经滚到顶 / 底时，把这一次滚动交给外层主内容区 ScrollViewer（ResultsScroll）；
        ///   3. 指针不在任何可滚动区域（左栏空白、水平胶囊条、右栏筛选面板的边角）时同样交给主内容区。
        /// Shift 按住 = 想水平滚动，原样放行给原生处理。
        /// </summary>
        private void Page_PreviewMouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            try
            {
                if (e.Handled || e.Delta == 0) return;
                if (System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift)) return;

                _wheelChain.Clear();
                CollectScrollViewers(e.OriginalSource as DependencyObject, _wheelChain);

                ScrollViewer? inner = null;
                for (int i = 0; i < _wheelChain.Count; i++)
                {
                    if (CanScrollFurther(_wheelChain[i], e.Delta)) { inner = _wheelChain[i]; break; }
                }

                // 最内层自己就能滚：不吞事件，交给 WPF 原生滚动（原生步长）
                if (inner != null && _wheelChain.Count > 0 && ReferenceEquals(inner, _wheelChain[0]))
                {
                    Utilities.Logger.LogInfo(
                        $"[Wheel] native target={Describe(inner)} delta={e.Delta} " +
                        $"offset={inner.VerticalOffset:0.#}/{inner.ScrollableHeight:0.#} chain={DescribeChain()}");
                    return;
                }

                // 内层已到边界（或指针根本不在可滚动区域）：剩余滚动转交外层主滚动
                var outer = inner ?? ResultsScroll;
                if (outer == null || !CanScrollFurther(outer, e.Delta))
                {
                    Utilities.Logger.LogInfo($"[Wheel] no-op delta={e.Delta} chain={DescribeChain()}");
                    return;
                }

                double before = outer.VerticalOffset;
                double requested = ScrollByNotches(outer, e.Delta);
                e.Handled = true;

                Utilities.Logger.LogInfo(
                    $"[Wheel] forwarded target={Describe(outer)} delta={e.Delta} " +
                    $"offset={before:0.#}->requested {requested:0.#}/{outer.ScrollableHeight:0.#} " +
                    $"chain={DescribeChain()}");
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "ResourcesPage wheel");
            }
        }

        /// <summary>[Wheel] 埋点：主内容区 ScrollViewer 的 VerticalOffset 变化（只在真的变了时写，避免刷屏）。</summary>
        private void ResultsScroll_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            try
            {
                double offset = e.VerticalOffset;
                if (!double.IsNaN(_lastScrollLoggedOffset) && Math.Abs(_lastScrollLoggedOffset - offset) < 0.01) return;
                _lastScrollLoggedOffset = offset;

                Utilities.Logger.LogInfo(
                    $"[Wheel] main-scroll offset={offset:0.#} change={e.VerticalChange:+0.#;-0.#} " +
                    $"scrollable={e.ExtentHeight - e.ViewportHeight:0.#} viewport={e.ViewportHeight:0.#} extent={e.ExtentHeight:0.#}");
            }
            catch { }
        }

        /// <summary>从指针下的元素往上收集所有可见的 ScrollViewer（内 -&gt; 外）。</summary>
        private static void CollectScrollViewers(DependencyObject? source, List<ScrollViewer> into)
        {
            var cur = source;
            int guard = 0;
            while (cur != null && guard++ < 256)
            {
                if (cur is ScrollViewer sv && sv.IsVisible) into.Add(sv);

                DependencyObject? parent = null;
                try { parent = VisualTreeHelper.GetParent(cur); } catch { parent = null; }
                if (parent == null)
                {
                    try { parent = LogicalTreeHelper.GetParent(cur); } catch { parent = null; }
                }
                cur = parent;
            }
        }

        /// <summary>这个 ScrollViewer 还能不能朝滚轮方向继续滚（比较 VerticalOffset 与 ScrollableHeight）。</summary>
        private static bool CanScrollFurther(ScrollViewer sv, int delta)
        {
            double extent = sv.ScrollableHeight;
            if (double.IsNaN(extent) || extent <= 0.5) return false;
            return delta < 0 ? sv.VerticalOffset < extent - 0.5 : sv.VerticalOffset > 0.5;
        }

        /// <summary>
        /// 按「一格滚轮 = WheelScrollLines 行、一行 16px」折算（与 WPF 原生
        /// CanContentScroll=false 的步长一致）；系统设成「整页滚动」时按视口高度走。
        /// </summary>
        private static double ScrollByNotches(ScrollViewer sv, int delta)
        {
            double notches = delta / 120.0;

            int lines = 3;
            try { lines = SystemParameters.WheelScrollLines; } catch { lines = 3; }

            double step = lines == -1
                ? Math.Max(1.0, sv.ViewportHeight)
                : 16.0 * (lines <= 0 ? 3 : lines);

            double max = sv.ScrollableHeight;
            double to = sv.VerticalOffset - notches * step;
            if (to < 0) to = 0;
            if (max > 0 && to > max) to = max;
            sv.ScrollToVerticalOffset(to);
            return to;   // 返回请求值（ScrollToVerticalOffset 是异步的，实际值看 ScrollChanged 那行）
        }

        private static string Describe(ScrollViewer sv)
            => string.IsNullOrEmpty(sv.Name) ? sv.GetType().Name : sv.Name;

        private string DescribeChain()
        {
            if (_wheelChain.Count == 0) return "[]";
            var sb = new System.Text.StringBuilder("[");
            for (int i = 0; i < _wheelChain.Count; i++)
            {
                if (i > 0) sb.Append("<-");
                sb.Append(Describe(_wheelChain[i]));
            }
            sb.Append(']');
            return sb.ToString();
        }

        #endregion

        /// <summary>把资源页恢复到「精选」态，并松开外壳的整页浮层开关。</summary>
        private void ExitFullPageOverlay()
        {
            if (DataContext is ViewModels.ResourcesViewModel vm) vm.IsFullPageView = false;

            if (Application.Current?.MainWindow?.DataContext is ViewModels.MainViewModel main)
                main.IsGlobalResourcesOverlayActive = false;
        }

        /// <summary>侧栏折叠小按钮。</summary>
        private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ResourcesViewModel vm)
                vm.IsSidebarCollapsed = !vm.IsSidebarCollapsed;
        }

        /// <summary>点卡片 = 打开详情（Axolotl 的卡片整块可点）。</summary>
        private void ProjectCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Models.Ecosystem.ModProject project &&
                DataContext is ViewModels.ResourcesViewModel vm)
            {
                vm.ViewDetailCommand.Execute(project);
            }
        }

        /// <summary>「所有来源」下拉：Modrinth / CurseForge（走 VM 已有的 SetSource）。</summary>
        private void SourceSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!IsLoaded) return;   // 初始化时 XAML 设的 SelectedIndex=0 不触发切换
            if (sender is not ComboBox combo || combo.SelectedIndex < 0) return;
            if (DataContext is not ViewModels.ResourcesViewModel vm) return;

            if (combo.SelectedIndex == 1 && !Services.Ecosystem.CurseForgeService.IsConfigured)
            {
                Controls.iOS26Dialog.Show("CurseForge 需要自己的 API Key（免费申请：console.curseforge.com）。请到「设置 → 下载 → CurseForge API Key」填写后再切换。",
                    "CurseForge", Controls.DialogIcon.Warning);
                combo.SelectedIndex = 0;   // 回退到 Modrinth
                return;
            }

            vm.SetSource(combo.SelectedIndex);
        }

        /// <summary>鼠标悬停结果行：按需拉详情补齐标签 / 更新时间（复用详情页的预加载缓存）。</summary>
        private void ProjectRow_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ViewModels.ResourceRowItem row &&
                DataContext is ViewModels.ResourcesViewModel vm)
            {
                vm.RequestRowEnrichment(row);
            }
        }

        #region 自检（TSURU_SELFTEST=resourcespage）

        private void RunResourcesSelfTest()
        {
            string shotDir = Environment.GetEnvironmentVariable("TSURU_SHOT_DIR") ?? string.Empty;
            var t = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(600),
                DispatcherPriority.Background, (a, b) => { }, Dispatcher);
            int step = 0;
            t.Tick += (s, e) =>
            {
                step++;
                if (DataContext is not ViewModels.ResourcesViewModel vm) { t.Stop(); return; }
                try
                {
                    switch (step)
                    {
                        case 1:
                            Log($"[ResourcesSelfTest] 1 初始态 IsFullPageView={vm.IsFullPageView} IsSidebarCollapsed={vm.IsSidebarCollapsed} 类别={vm.SelectedCategory}");
                            Log($"[ResourcesSelfTest] 1b **展开态** 折叠按钮 HA={((ResourcesRoot?.FindName("SidebarToggleBtn") as FrameworkElement)?.HorizontalAlignment).ToString()} （期望 Right=用户红框位置）");
                            Shot(shotDir, "01-初始");
                            break;
                        case 2:
                            // 独立模式（collapsonly）：跳过全部测量，只做「收起 → 截图 → 停」。
                            // 普通模式（resourcespage）：先收起 + 测量 + 截图，然后继续走完整流程。
                            bool collapsonly = Environment.GetEnvironmentVariable("TSURU_SELFTEST") == "collapsonly";
                            if (!collapsonly)
                            {
                                vm.ToggleSidebarCommand.Execute(null);
                                Log($"[ResourcesSelfTest] 2 收起左侧栏 IsSidebarCollapsed={vm.IsSidebarCollapsed}");
                                var b = ResourcesRoot?.FindName("SidebarToggleBtn") as FrameworkElement;
                                if (b != null)
                                {
                                    var pt = b.TransformToAncestor(ResourcesRoot).Transform(new Point(0, 0));
                                    Log($"[ResourcesSelfTest] 2b 折叠按钮 SidebarToggleBtn " +
                                        $"ActualW={b.ActualWidth:0.0} ActualH={b.ActualHeight:0.0} " +
                                        $"HA={b.HorizontalAlignment} " +
                                        $"在 ResourcesRoot 内的左缘={pt.X:0.0} 上缘={pt.Y:0.0}");
                                    Log($"[ResourcesSelfTest] 2c SidebarCard 容器信息 " +
                                        $"Width={SidebarCard?.ActualWidth:0.0} HA={SidebarCard?.HorizontalAlignment} " +
                                        $"Margin={SidebarCard?.Margin}");
                                }
                                // 顺便量一个图标的实际位置，看看跟按钮差多远
                                RadioButton? firstIcon = null;
                                foreach (var rb in FindVisualChildren<RadioButton>(SidebarCard ?? (DependencyObject)ResourcesRoot))
                                {
                                    if (rb.Visibility == Visibility.Visible && rb.ActualWidth > 0)
                                    {
                                        firstIcon = rb;
                                        break;
                                    }
                                }
                                if (firstIcon != null)
                                {
                                    var pt2 = firstIcon.TransformToAncestor(ResourcesRoot).Transform(new Point(0, 0));
                                    Log($"[ResourcesSelfTest] 2d 第一个**可见** RadioButton " +
                                        $"Content=\"{firstIcon.Content}\" " +
                                        $"ActualW={firstIcon.ActualWidth:0.0} ActualH={firstIcon.ActualHeight:0.0} " +
                                        $"HA={firstIcon.HorizontalAlignment} " +
                                        $"在 ResourcesRoot 内的左缘={pt2.X:0.0} 上缘={pt2.Y:0.0}");
                                }
                                // 也量一下 SidebarCollapsed 那个 StackPanel 是否真的 Visible 了
                                Log($"[ResourcesSelfTest] 2e SidebarCollapsed Visibility={SidebarCollapsed.Visibility} " +
                                    $"ActualW={SidebarCollapsed.ActualWidth:0.0}");
                            }
                            else
                            {
                                vm.ToggleSidebarCommand.Execute(null);
                                Log($"[CollapseOnly] 收起 IsSidebarCollapsed={vm.IsSidebarCollapsed}");
                                var btn = ResourcesRoot?.FindName("SidebarToggleBtn") as FrameworkElement;
                                Log($"[CollapseOnly] **折叠态** 折叠按钮 HA={btn?.HorizontalAlignment.ToString() ?? "?"} （期望 Center=用户要的中间）");
                                // 让折叠动画 + SettleSidebarText 都跑完，再截图
                                var t2 = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
                                t2.Interval = TimeSpan.FromMilliseconds(1500);
                                t2.Tick += (_, _) =>
                                {
                                    t2.Stop();
                                    Log($"[CollapseOnly] 截图前 SidebarCollapsed.Visibility={SidebarCollapsed.Visibility} " +
                                        $"SidebarCard.ActualWidth={SidebarCard.ActualWidth:0.0}");
                                    Shot(shotDir, "collapsonly");
                                    t.Stop();
                                };
                                t2.Start();
                                return;
                            }
                            break;
                        case 3:
                            Shot(shotDir, "02-左栏收起");
                            // 同时收起右栏（MainWindow 的 InfoPanelToggle）
                            if (Application.Current.MainWindow is MainWindow mw)
                                mw.ToggleInfoPanelForSelfTest();
                            Log($"[ResourcesSelfTest] 3 收起右栏");
                            break;
                        case 4:
                            Shot(shotDir, "03-两边都收起");
                            // 顺便量外壳 / 资源页关键尺寸
                            if (Application.Current.MainWindow is MainWindow mw3)
                            {
                                Log($"[ResourcesSelfTest] 3b 外壳右信息面板列宽={mw3.InfoPanelColumnWidth:0.0}（期望 0）");
                            }
                            // 找到 TrendingMods 对应的 ItemsControl（XAML 上第 2 个 —— 第一个是搜索的、第二个才是热门）
                            var panels = FindVisualChildren<ItemsControl>(ResultsStack ?? (DependencyObject)ResourcesRoot).Take(6).ToList();
                            var icWidths = string.Join(" | ", panels.Select(p => $"#{p.ActualWidth:0.0}"));
                            Log($"[ResourcesSelfTest] 3c 资源页关键尺寸 " +
                                $"ResourcesRoot={ResourcesRoot?.ActualWidth:0.0} " +
                                $"ResultsScroll={ResultsScroll?.ActualWidth:0.0} " +
                                $"ResultsStack={ResultsStack?.ActualWidth:0.0} " +
                                $"ItemsControl宽度序列={icWidths} " +
                                $"SidebarCard={SidebarCard?.ActualWidth:0.0}");
                            // 展开两侧回到原状，再触发「展开全部」
                            if (Application.Current.MainWindow is MainWindow mw2)
                                mw2.ToggleInfoPanelForSelfTest();
                            vm.ToggleSidebarCommand.Execute(null);
                            Log($"[ResourcesSelfTest] 4 恢复两侧");
                            break;
                        case 5:
                            Shot(shotDir, "04-恢复");
                            break;
                        case 6:
                            vm.ViewMoreCommand.Execute(null);
                            Log($"[ResourcesSelfTest] 5 点击展开全部 IsFullPageView={vm.IsFullPageView}");
                            break;
                        case 7:
                            Shot(shotDir, "05-展开全部");
                            // ⭐ Bug 4 测试：在展开全部状态下切换分类（模拟点顶栏 资源包）
                            vm.SelectedCategory = "resourcepack";
                            vm.SwitchCategoryCommand.Execute("resourcepack");
                            Log($"[ResourcesSelfTest] 6 展开全部中切到 resourcepack IsFullPageView={vm.IsFullPageView} 期望=True");
                            break;
                        case 8:
                            Shot(shotDir, "06-展开全部中切分类");
                            vm.ViewMoreCommand.Execute(null); // 退回精选
                            break;
                        case 9:
                            Log("[ResourcesSelfTest] ✅ 完成（继续等对话框自检）");
                            // 整轮自检走完，再等 30s 弹一个示例对话框（给 --screenshot 抓拍用）
                            var tDlg = new DispatcherTimer(DispatcherPriority.Background, Dispatcher);
                            tDlg.Interval = TimeSpan.FromSeconds(30);
                            tDlg.Tick += (_, _) =>
                            {
                                tDlg.Stop();
                                // modeless=true：非模态弹出，DispatcherTimer 和 RenderTargetBitmap 都能继续工作
                                var dlg = Controls.iOS26Dialog.ShowCore(
                                    Application.Current.MainWindow,
                                    "这是用 iOS26Dialog 弹的提示框，结构 / 颜色都按 Axolotl「选择关闭 Axolotl Launcher 的方式」那张图还原。",
                                    "风格预览",
                                    Controls.DialogIcon.Info,
                                    Controls.DialogButtons.OK,
                                    "记住我的选择");
                                if (dlg != null)
                                {
                                    // 等动画播完 + 一帧布局
                                    dlg.UpdateLayout();
                                    ShotDialog(dlg, Environment.GetEnvironmentVariable("TSURU_SHOT_DIR") ?? string.Empty, "99-dialog");
                                }
                                t.Stop();
                            };
                            tDlg.Start();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[ResourcesSelfTest] 第 {step} 步: {ex.GetType().Name} {ex.Message}");
                    t.Stop();
                }
                if (step > 102) t.Stop();

                // 独立调试入口：TSURU_SELFTEST=collapsonly —— 只跑「收起 → 截图」，
                // 方便把 chevron 位置看清楚，不被 10 步自检的中间状态污染。
                if (Environment.GetEnvironmentVariable("TSURU_SELFTEST") == "collapsonly" && step == 2)
                {
                    vm.ToggleSidebarCommand.Execute(null);
                    Log($"[CollapseOnly] 收起 IsSidebarCollapsed={vm.IsSidebarCollapsed}");
                    Shot(Environment.GetEnvironmentVariable("TSURU_SHOT_DIR") ?? string.Empty, "collapsonly");
                    t.Stop();
                }
            };
            t.Start();
        }

        /// <summary>递归找指定类型的所有可视子元素。</summary>
        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? root) where T : DependencyObject
        {
            if (root == null) yield break;
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (c is T t) yield return t;
                foreach (var sub in FindVisualChildren<T>(c)) yield return sub;
            }
        }

        private static void Log(string m)
        {
            Utilities.Logger.LogInfo(m);
            System.Diagnostics.Debug.WriteLine(m);
        }
        private static void Shot(string dir, string name)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            try { System.IO.Directory.CreateDirectory(dir); TsuruLauncher.App.CaptureToPng(System.IO.Path.Combine(dir, name + ".png")); Log($"[ResourcesSelfTest] 📷 {name}"); }
            catch (Exception ex) { Log($"[ResourcesSelfTest] 截图失败: {ex.Message}"); }
        }

        /// <summary>把任意 Window 单独渲染成 PNG（用于对话框自检）。</summary>
        private static void ShotDialog(Window w, string dir, string name)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, name + ".png");
                w.UpdateLayout();
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(w);
                int pw = (int)Math.Ceiling(w.ActualWidth * dpi.DpiScaleX);
                int ph = (int)Math.Ceiling(w.ActualHeight * dpi.DpiScaleY);
                if (pw <= 0 || ph <= 0) { Log($"[ResourcesSelfTest] 截图失败: {w.Title} 尺寸 {w.ActualWidth}x{w.ActualHeight}"); return; }
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(pw, ph, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(w);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using var fs = System.IO.File.Create(path);
                enc.Save(fs);
                Log($"[ResourcesSelfTest] 📷 {name} {pw}x{ph}");
            }
            catch (Exception ex) { Log($"[ResourcesSelfTest] 截图失败: {ex.Message}"); }
        }

        #endregion
    }
}
