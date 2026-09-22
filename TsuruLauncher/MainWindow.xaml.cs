using System;
using System.Collections.Generic;
using System.Windows;
using Wpf.Ui.Controls;
using System.Windows.Controls;
using TsuruLauncher.Views;
using TsuruLauncher.Controls;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TsuruLauncher.Services.Animation;

namespace TsuruLauncher
{
    public partial class MainWindow : FluentWindow
    {
        private DispatcherTimer? _preloadDismissTimer;
        private bool _isNavigatingBack;

        // Top-level pages are cached singletons so navigation (and language
        // switches) never throw away the user's state (search, scroll, etc.).
        private readonly Dictionary<string, System.Windows.Controls.Page> _pageCache = new();

        public MainWindow()
        {
            InitializeComponent();
            this.KeyDown += Window_KeyDown;

            // 动效偏好一次性打在日志里：默认全量播放，只有显式 opt-out 才降级
            // （系统 ClientAreaAnimation / MenuAnimation 在远程桌面等会话里默认就是 False，
            //   以前直接拿它当默认值会让整套界面变成硬切）。
            Utilities.Logger.LogInfo("[Motion] " + AxolotlMotion.DescribeMotionPreference());
            
            RootFrame.Navigating += RootFrame_Navigating;
            RootFrame.Navigated += RootFrame_Navigated;
            this.Loaded += MainWindow_Loaded;
            this.Activated += MainWindow_Activated;

            // When the language changes, re-navigate the current page so
            // DynamicResource texts refresh immediately; cached pages keep state.
            TsuruLauncher.App.LanguageChanged += () =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (RootFrame.Content is System.Windows.Controls.Page current)
                    {
                        RootFrame.Navigate(current);
                    }
                });
            };
            
            var vm = this.DataContext as ViewModels.MainViewModel;
            if (vm != null)
            {
                vm.RequestNavigation += (page) => RootFrame.Navigate(page);
                vm.RequestGoBack += () => 
                {
                    if (RootFrame.CanGoBack) RootFrame.GoBack();
                };

                vm.NotificationService.OnShowNotification += (msg) => 
                {
                    Dispatcher.Invoke(() => 
                    {
                        NotificationToast? toastRef = null;
                        toastRef = new NotificationToast(msg, () => 
                        {
                            if (toastRef != null && NotificationContainer.Children.Contains(toastRef))
                                NotificationContainer.Children.Remove(toastRef);
                        });
                        NotificationContainer.Children.Add(toastRef);
                    });
                };

                vm.PreloadProgressChanged += (progress, status) =>
                {
                    Dispatcher.Invoke(() => UpdatePreloadNotification(progress, status));
                };

                vm.PreloadCompleted += () =>
                {
                    Dispatcher.Invoke(() => ShowPreloadComplete());
                };

                // 进入 / 离开「资源整页浮层」时同步右侧边缘热区的可点状态
                vm.PropertyChanged += (s, e) =>
                {
                    if (e.PropertyName == nameof(ViewModels.MainViewModel.IsHomeEditing) ||
                        e.PropertyName == nameof(ViewModels.MainViewModel.IsMinimalHome))
                    {
                        // ⚠ 这里**绝不能**用 animate:true 去动右栏列宽。
                        //   切主页模式本身已经在跑「两棵主页根交叉淡入 + 卡片错峰」一大串动画，
                        //   再叠一条 320ms 的列宽动画 → 实测单帧 worst=494.9ms、总时长 1.26s，
                        //   用户直接感觉「卡死」。改成无动画直接落终态。
                        if (DataContext is ViewModels.MainViewModel wvm && wvm.IsHomeEditing && !wvm.IsMinimalHome)
                            ApplyInfoPanelState(expanded: true, animate: false);
                        SyncInfoPanelPageContent();
                        UpdatePillForCurrentPage();
                        return;
                    }

                    if (e.PropertyName == nameof(ViewModels.MainViewModel.IsGlobalResourcesOverlayActive))
                    {
                        ApplyShellPanels(visible: !vm.IsGlobalResourcesOverlayActive, animate: true);
                        SyncInfoPanelEdge();
                    }
                };
            }
        }

        #region Window chrome

        private void TopBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ToggleMaximize();
                return;
            }
            try { DragMove(); } catch { }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
            => ToggleMaximize();

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => Close();

        private void ToggleMaximize()
            => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

        #endregion

        #region Navigation rail

        /// <summary>SyncNavRailSelection 程序化改 IsChecked 时置位，避免自己触发一次导航。</summary>
        private bool _navSelectionSync;

        /// <summary>刚由 Checked 处理过这次交互（同一次点击里 Click 会跟着来，别重复导航）。</summary>
        private bool _navCheckedThisClick;

        /// <summary>
        /// 左导航切换。挂在 <c>Checked</c>（不是 <c>Click</c>）上：
        /// 鼠标点击、键盘方向键、以及 UI Automation 的
        /// <c>SelectionItemPattern.Select()</c> 都会走到同一条导航路径
        /// （WPF 的 RadioButton 被 SelectionItemPattern 改 IsChecked 时不会抛 Click）。
        /// </summary>
        private void NavRail_Checked(object sender, RoutedEventArgs e)
        {
            // 初始化时 XAML 的 IsChecked="True" / 程序化同步选中态都不算导航
            if (!IsLoaded || _navSelectionSync) return;
            if (sender is not RadioButton rb || rb.Tag is not string tag) return;

            _navCheckedThisClick = true;
            Dispatcher.BeginInvoke(new Action(() => _navCheckedThisClick = false),
                System.Windows.Threading.DispatcherPriority.Input);

            NavigateFromRail(tag);
        }

        /// <summary>已经是当前项时再点一次不会再触发 Checked —— 这里保留"刷新当前页"的旧行为。</summary>
        private void NavRail_Click(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _navSelectionSync || _navCheckedThisClick) return;
            if (sender is RadioButton rb && rb.Tag is string tag) NavigateFromRail(tag);
        }

        private void NavigateFromRail(string tag)
        {
            try
            {
                MotionDiagnostics.NavigationStarted(tag.ToLower().Trim());
                NavigateTo(tag.ToLower().Trim());
                UpdateNavSlider(animate: true);
            }
            catch (Exception ex)
            {
                TsuruLauncher.Utilities.Logger.LogError(ex, "NavRail");
                iOS26Dialog.Show($"导航错误: {ex.Message}", "导航错误", DialogIcon.Error);
            }
        }

        /// <summary>
        /// 调试入口（<c>--page resource-detail</c>）用的假项目。
        /// 用 Modrinth 上的 Sodium —— 它同时有 Fabric / NeoForge / Quilt 三种 loader、
        /// 几百个游戏版本、三种发布通道都有，正好把三颗筛选下拉全点亮。
        /// </summary>
        private static Models.Ecosystem.ModProject DebugDetailProject => new()
        {
            Id = "AANobbMI",
            Name = "Sodium",
            Summary = "调试入口占位（真实数据由资源服务拉取）",
            Author = "jellysquid3",
            Platform = Models.Ecosystem.ProjectPlatform.Modrinth,
            Type = Models.Ecosystem.ProjectType.Mod,
            WebUrl = "https://modrinth.com/mod/sodium"
        };

        public void NavigateTo(string? tag)
        {            MotionDiagnostics.NavigationStarted(tag);

            // 任何导航都先复位「资源整页浮层」：否则在资源页点过「展开全部」之后，
            // 外壳的 DataTrigger 会一直隐藏左导航栏 / 右侧信息面板。
            ResetGlobalResourcesOverlay();

            System.Windows.Controls.Page? page = tag switch
            {
                "home" => GetCachedPage("home", () => new Views.HomePage()),
                "resources" => GetCachedPage("resources", () => new Views.ResourcesPage()),
                "download" => GetCachedPage("download", () => new Views.DownloadPage()),
                "lab" => GetCachedPage("lab", () => new Views.LabPage()),
                "settings" => GetCachedPage("settings", () => new Views.SettingsPage()),
                // 调试用：直接进「版本选择」页（它平时只能从首页点进去），方便验证右栏文件夹列表
                "version-selector" => GetCachedPage("version-selector",
                    () => new Views.VersionSelectorPage(_ => { })),
                // 调试用：直接进「资源详情」页（平时只能从资源列表点进去），
                // 配合 TSURU_SELFTEST=resourcedetail 验证 hero 按钮 / 三颗筛选下拉 / 分页。
                "resource-detail" => new Views.ResourceDetailPage(DebugDetailProject),
                _ => null
            };

            if (page == null) return;

            SyncNavRailSelection(tag);

            if (ReferenceEquals(RootFrame.Content, page))
            {
                // Already on this page: refresh data instead of re-navigating
                if (page is Views.HomePage hp) hp.RefreshOverviewData();
                UpdateNavSlider(animate: true);
                return;
            }

            // Keep the home overview fresh whenever we land on it
            if (page is Views.HomePage homePage) homePage.RefreshOverviewData();

            RootFrame.Navigate(page);
            UpdateNavSlider(animate: true);
        }

        /// <summary>让左导航的选中态跟随程序化导航（通知跳转、F12、语言切换等）。</summary>
        private void SyncNavRailSelection(string? tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return;
            string key = tag.ToLower().Trim();
            foreach (var child in NavRail.Children)
            {
                if (child is RadioButton rb && rb.Tag is string t && t.ToLower().Trim() == key)
                {
                    if (rb.IsChecked != true)
                    {
                        _navSelectionSync = true;
                        try { rb.IsChecked = true; }
                        finally { _navSelectionSync = false; }
                    }
                    return;
                }
            }
        }

        #region Nav rail sliding indicator（Axolotl NavRail.vue:104-115）

        private double _navSliderY = double.NaN;
        private double _navSliderH = double.NaN;
        private bool _navSliderReady;

        /// <summary>胶囊正在播位移动画（NavRailHost.SizeChanged 的被动刷新不要打断它）。</summary>
        private bool _navSliderAnimating;

        /// <summary>
        /// 导航滑动胶囊（Axolotl NavRail.vue:104-115 的 WPF 落地）。
        ///
        /// **旧实现为什么会抖（已废弃）**：把原版的 <c>top/bottom 各 150ms + STAGGER_DELAY=120ms</c>
        /// 硬译成 <c>TranslateY</c>（承担 top，下滑时 <c>BeginTime = 120ms</c>）
        /// + <c>ScaleY</c>（<c>RenderTransformOrigin=0,0</c>，承担 bottom 的"先拉伸"）两段合成。
        /// 三个问题叠在一起：
        ///   1. 下滑时 <c>TranslateY</c> 有 120ms 延迟，而 <c>Margin.Top</c> 是**立刻**写到新位置的 ——
        ///      前 120ms 胶囊已经"瞬移"到新格子顶端，然后才被 ScaleY 拉伸回来 → 从上往下点就是那一抖；
        ///   2. 上滑时延迟在另一端、ScaleY 从顶边向下长，两个方向的合成曲线完全不同 → 双向观感不一致；
        ///   3. <c>NavSlider.Height</c> 被立刻改写 + ScaleY 变化会改变 <c>NavRailHost</c> 的
        ///      desired size，触发 <c>SizeChanged → UpdateNavSlider(animate:false)</c>，
        ///      把正在跑的动画硬拽回 0 —— 自己打断自己的反馈环。
        ///
        /// **新实现（双向一致、不抖）**：只用一个 <see cref="TranslateTransform"/> 的 Y。
        ///   * <c>Margin.Top</c> 立刻写成新位置 newY，位移全部由 TranslateTransform 承担；
        ///   * 变换起点 = <c>当前动画值 + (上一次目标 - 新目标)</c>，
        ///     即"从眼下看到的位置连续插值"，所以连点 / 反向点 / 跨多项跳转都不跳；
        ///   * 两个方向都是同一条 150ms <c>cubic-bezier(0.4, 0, 0.2, 1)</c>，**没有 BeginTime 错峰**；
        ///   * <c>ScaleY</c> 恒等于 1，不再做任何缩放补偿；
        ///   * <c>Height</c> 只在真的不一样时才瞬时改（不参与动画）——
        ///     动画 Height 会改 NavRailHost 的 desired size 并触发上面第 3 条的反馈环；
        ///     导航按钮模板一致，实际高度本来就相同；
        ///   * 结束时摘掉时钟并把值落到 0，不留 HoldEnd 残留。
        /// </summary>
        private void UpdateNavSlider(bool animate)
        {
            try
            {
                RadioButton? selected = null;
                foreach (var child in NavRail.Children)
                {
                    if (child is RadioButton rb && rb.IsChecked == true && rb.Visibility == Visibility.Visible)
                    {
                        selected = rb;
                        break;
                    }
                }
                if (selected == null || selected.ActualHeight < 1 || selected.ActualWidth < 1) return;

                var origin = selected.TransformToAncestor(NavRailHost).Transform(new Point(0, 0));
                double w = selected.ActualWidth;
                double h = selected.ActualHeight;
                double newY = origin.Y;

                bool first = double.IsNaN(_navSliderY) || !_navSliderReady;
                double prevY = first ? newY : _navSliderY;
                double prevH = (double.IsNaN(_navSliderH) || first) ? h : _navSliderH;

                // SizeChanged 之类的被动刷新：目标没变就别动它（尤其别打断正在跑的动画）
                if (!animate && !first &&
                    Math.Abs(prevY - newY) < 0.5 && Math.Abs(prevH - h) < 0.5 &&
                    Math.Abs(origin.X - NavSlider.Margin.Left) < 0.5)
                {
                    return;
                }

                using var _ = MotionPerf.Measure(animate ? "nav-slider" : "nav-slider-passive",
                    animate ? null : "reason=layout-refresh");

                // 先读出"动画中的当前值"，再清时钟 —— 清完读到的就是基值了
                double animatedOffsetY = NavSliderTranslate.Y;
                double animatedHeight = NavSlider.Height;

                NavSliderTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                NavSliderScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                NavSlider.BeginAnimation(FrameworkElement.HeightProperty, null);
                NavSliderScale.ScaleY = 1.0;      // 永远不再用缩放补偿

                NavSlider.Width = w;
                NavSlider.Margin = new Thickness(origin.X, newY, 0, 0);   // 新格子（位移交给变换）
                NavSlider.Height = h;                                      // 高度不参与动画
                _navSliderY = newY;
                _navSliderH = h;
                _navSliderReady = true;

                // 眼下胶囊顶端在哪：上一次目标位置 + 当前动画偏移
                double visualTop = prevY + (double.IsNaN(animatedOffsetY) ? 0.0 : animatedOffsetY);
                double fromY = visualTop - newY;

                bool skipMotion = !animate || !PageTransition.AnimationsEnabled || Math.Abs(fromY) < 0.5;

                if (skipMotion)
                {
                    NavSliderTranslate.Y = 0;

                    if (first)
                    {
                        // 首次出现：opacity 250ms cubic-bezier(0.5, 0, 0.2, 1) 延迟 50ms
                        var fade = new DoubleAnimation(0, 1, AxolotlMotion.Ms(AxolotlMotion.NavSliderFadeMs))
                        {
                            EasingFunction = AxolotlMotion.NavSliderFadeEase,
                            BeginTime = TimeSpan.FromMilliseconds(AxolotlMotion.NavSliderFadeDelayMs),
                        };
                        NavSlider.BeginAnimation(UIElement.OpacityProperty, fade);
                    }
                    else
                    {
                        NavSlider.BeginAnimation(UIElement.OpacityProperty, null);
                        NavSlider.Opacity = 1;
                    }
                    return;
                }

                NavSliderTranslate.Y = fromY;
                _navSliderAnimating = true;

                var move = new DoubleAnimation(fromY, 0, AxolotlMotion.Ms(AxolotlMotion.NavSliderMs))
                {
                    EasingFunction = AxolotlMotion.EaseInOut,
                };
                move.Completed += (s2, e2) =>
                {
                    NavSliderTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                    NavSliderTranslate.Y = 0;      // 结束归位，不留 HoldEnd 残留
                    _navSliderAnimating = false;
                };
                NavSliderTranslate.BeginAnimation(TranslateTransform.YProperty, move);

                Utilities.Logger.LogInfo(
                    $"[MotionPerf] nav slider {(newY > prevY ? "down" : "up")} delta={newY - prevY:+0.#;-0.#}px " +
                    $"from={fromY:+0.#;-0.#}px->0 {AxolotlMotion.NavSliderMs}ms cubic-bezier(0.4,0,0.2,1) " +
                    $"symmetric=true stagger=0 scaleY=1 (was: BeginTime={AxolotlMotion.NavSliderStaggerMs}ms + ScaleY stretch)");
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "UpdateNavSlider");
            }
        }

        #endregion

        #endregion

        private System.Windows.Controls.Page GetCachedPage(string key, Func<System.Windows.Controls.Page> factory)
        {
            if (!_pageCache.TryGetValue(key, out var page))
            {
                page = factory();
                _pageCache[key] = page;
            }
            return page;
        }

        /// <summary>
        /// 复位「资源页整页浮层」：左导航栏 / 右侧信息面板恢复显示，资源页回到精选态。
        /// </summary>
        private void ResetGlobalResourcesOverlay()
        {
            if (DataContext is ViewModels.MainViewModel vm)
                vm.IsGlobalResourcesOverlayActive = false;

            if (TryGetResourcesViewModel(out var resources))
                resources.IsFullPageView = false;
        }

        /// <summary>拿到资源页的 ViewModel（缓存页优先，其次当前页）；拿不到就返回 false。</summary>
        private bool TryGetResourcesViewModel(out ViewModels.ResourcesViewModel resources)
        {
            if (_pageCache.TryGetValue("resources", out var cached) &&
                cached is Views.ResourcesPage cachedPage &&
                cachedPage.DataContext is ViewModels.ResourcesViewModel cachedVm)
            {
                resources = cachedVm;
                return true;
            }

            if (RootFrame.Content is Views.ResourcesPage current &&
                current.DataContext is ViewModels.ResourcesViewModel currentVm)
            {
                resources = currentVm;
                return true;
            }

            resources = null!;
            return false;
        }

        #region 右信息面板：内容按当前页面切换

        /// <summary>「资源」页在右栏展示的筛选卡片（懒建，跨导航复用同一个实例）。</summary>
        private Controls.ResourcesFilterPanel? _infoPanelFilter;
        private bool _widgetEditorShown;
        private Controls.VersionFolderPanel? _versionFolderPanel;
        private Controls.ResourceDetailSidePanel? _detailSidePanel;

        /// <summary>
        /// 右信息面板的内容跟着当前页面走：
        ///   * 「资源」页 -&gt; 筛选卡片（<see cref="Controls.ResourcesFilterPanel"/>，
        ///     DataContext 用该页自己的 <c>ResourcesViewModel</c>）；
        ///   * 其它页面 -&gt; 账号 / 游玩洞察 / 每日小挑战 / 新闻。
        ///
        /// 切换本身**不播动画**：它和页面进场发生在同一帧，而面板此刻正在跑 320ms 的列宽过渡，
        /// 再叠一层内容动画只会把那一帧推过 3ms 同步预算。
        /// </summary>
        private void SyncInfoPanelPageContent()
        {
            if (InfoPanelHomeSections == null || InfoPanelResourcesFilterHost == null) return;

            if (InfoPanelWidgetEditorHost == null || InfoPanelFolderListHost == null ||
                InfoPanelResourceDetailHost == null) return;

            // ①-c 「资源详情」页：右栏放详情侧板（兼容性 / 相关链接 / 标签 / 作者 / 信息）
            if (RootFrame.Content is Views.ResourceDetailPage detailPage)
            {
                if (_detailSidePanel == null)
                {
                    _detailSidePanel = new Controls.ResourceDetailSidePanel();
                    InfoPanelResourceDetailHost.Content = _detailSidePanel;
                }
                if (!ReferenceEquals(_detailSidePanel.DataContext, detailPage.DataContext))
                    _detailSidePanel.DataContext = detailPage.DataContext;

                InfoPanelWidgetEditorHost.Visibility = Visibility.Collapsed;
                InfoPanelResourcesFilterHost.Visibility = Visibility.Collapsed;
                InfoPanelFolderListHost.Visibility = Visibility.Collapsed;
                InfoPanelHomeSections.Visibility = Visibility.Collapsed;
                InfoPanelResourceDetailHost.Visibility = Visibility.Visible;
                return;
            }

            InfoPanelResourceDetailHost.Visibility = Visibility.Collapsed;

            // ①-b 「版本选择」页：右栏放文件夹列表
            if (RootFrame.Content is Views.VersionSelectorPage)
            {
                if (_versionFolderPanel == null)
                {
                    _versionFolderPanel = new Controls.VersionFolderPanel();
                    InfoPanelFolderListHost.Content = _versionFolderPanel;
                }
                _versionFolderPanel.Reload();
                InfoPanelWidgetEditorHost.Visibility = Visibility.Collapsed;
                InfoPanelResourcesFilterHost.Visibility = Visibility.Collapsed;
                InfoPanelHomeSections.Visibility = Visibility.Collapsed;
                InfoPanelFolderListHost.Visibility = Visibility.Visible;
                return;
            }

            InfoPanelFolderListHost.Visibility = Visibility.Collapsed;

            // ① 首页 + 编辑态 + 网格视图 → 右栏换成小组件编辑器
            //    （添加 / 移除 / 改尺寸都在这里做；网格视图上不再铺内联的添加框）
            bool showWidgetEditor = RootFrame.Content is Views.HomePage &&
                                    DataContext is ViewModels.MainViewModel hvm &&
                                    hvm.IsHomeEditing && !hvm.IsMinimalHome;

            // 右栏内容切换必须带动画 —— 之前是 Visibility 硬切，用户反馈「多生硬啊」。
            // 用外壳同一套 PlayPopup（淡入 + scale 0.97->1），进出都播；靠 _widgetEditorShown
            // 记状态，避免每次导航都重播一遍。
            if (showWidgetEditor != _widgetEditorShown)
            {
                _widgetEditorShown = showWidgetEditor;
                if (showWidgetEditor)
                {
                    InfoPanelResourcesFilterHost.Visibility = Visibility.Collapsed;
                    InfoPanelHomeSections.Visibility = Visibility.Collapsed;
                    InfoPanelWidgetEditorHost.Visibility = Visibility.Visible;
                    // 用 PlayPopup 的 **Axolotl 弹入**（淡入 + scale 0.96→1 + 过冲曲线
                    // cubic-bezier(0.15,1.4,0.64,0.96)）—— 和项目里其它浮层同一套手感。
                    // 但必须 useCache:false：它会走 CacheDuring 挂 BitmapCache，
                    // 而实测本机同时挂 15 个缓存 / 7.84M 像素时，一移动窗口就
                    // UCEERR_RENDERTHREADFAILURE。右栏这么小的区域不值得缓存。
                    InfoPanelWidgetEditorHost.Opacity = 0;
                    PageTransition.PlayPopup(InfoPanelWidgetEditorHost, show: true, fromScale: 0.96,
                        durationMs: AxolotlMotion.FloatingPanelMs, ease: AxolotlMotion.OvershootEase,
                        useCache: false);
                }
                else
                {
                    PageTransition.PlayPopup(InfoPanelWidgetEditorHost, show: false, fromScale: 0.98,
                        durationMs: AxolotlMotion.FloatingPanelLeaveMs, ease: AxolotlMotion.EaseInOut,
                        onCompleted: () => InfoPanelWidgetEditorHost.Visibility = Visibility.Collapsed,
                        useCache: false);
                }
            }

            if (showWidgetEditor)
            {
                InfoPanelWidgetEditorHost.Visibility = Visibility.Visible;
                InfoPanelResourcesFilterHost.Visibility = Visibility.Collapsed;
                InfoPanelHomeSections.Visibility = Visibility.Collapsed;
                return;
            }

            InfoPanelWidgetEditorHost.Visibility = Visibility.Collapsed;

            if (RootFrame.Content is Views.ResourcesPage && TryGetResourcesViewModel(out var resources))
            {
                if (_infoPanelFilter == null)
                {
                    _infoPanelFilter = new Controls.ResourcesFilterPanel();
                    InfoPanelResourcesFilterHost.Content = _infoPanelFilter;
                }

                if (!ReferenceEquals(_infoPanelFilter.DataContext, resources))
                    _infoPanelFilter.DataContext = resources;

                InfoPanelResourcesFilterHost.Visibility = Visibility.Visible;
                InfoPanelHomeSections.Visibility = Visibility.Collapsed;
            }
            else
            {
                InfoPanelResourcesFilterHost.Visibility = Visibility.Collapsed;
                InfoPanelHomeSections.Visibility = Visibility.Visible;
            }
        }

        #endregion

        /// <summary>Syncs the rail buttons with the user's hidden-page settings.</summary>
        public void RefreshNavigationVisibility()
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                var hiddenKeys = vm.ConfigService.Settings.HiddenPageKeys;
                foreach (var child in NavRail.Children)
                {
                    if (child is RadioButton rb && rb.Tag is string tag)
                    {
                        rb.Visibility = hiddenKeys.Contains(tag.ToLower())
                            ? Visibility.Collapsed
                            : Visibility.Visible;
                    }
                }
            }
        }

        #region Preload toast

        private void ShowPreloadNotification()
        {
            PreloadNotification.Visibility = Visibility.Visible;
            PageTransition.Play(PreloadNotification, TransitionType.SlideUp);
        }

        private void UpdatePreloadNotification(int progress, string status)
        {
            if (PreloadNotification.Visibility != Visibility.Visible)
                ShowPreloadNotification();

            PreloadNotifProgress.Value = progress;
            PreloadNotifDetail.Text = status;
        }

        private void ShowPreloadComplete()
        {
            PreloadNotifTitle.Text = "资源加载完成";
            PreloadNotifDetail.Text = "";
            PreloadNotifIconText.Text = "✓";
            PreloadNotifIcon.Background = Utilities.ThemeBrush.Accent;
            PreloadNotifProgress.Visibility = Visibility.Collapsed;

            _preloadDismissTimer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(3) };
            _preloadDismissTimer.Tick += (s, e) =>
            {
                _preloadDismissTimer.Stop();
                DismissPreloadNotification();
            };
            _preloadDismissTimer.Start();
        }

        private void DismissPreloadNotification()
        {
            PageTransition.Play(PreloadNotification, TransitionType.SlideDown, () =>
            {
                PreloadNotification.Visibility = Visibility.Collapsed;
            });
        }

        #endregion

        /// <summary>
        /// 换页**立即**发生 —— 这里不再 <c>e.Cancel</c> 拦下来做串行淡出。
        ///
        /// 旧做法（已废弃，串行两拍）：宿主 120ms 淡出 → Completed 里重放导航 → 新页 180/200ms 进场，
        /// 两拍相加 320ms，且前 120ms 只有画面变暗、新内容一动不动 = 用户说的「切页很僵硬、感知延迟 140ms+」。
        ///
        /// 新做法（重叠）：换页当场完成，离场补偿与新页进场在 <see cref="RootFrame_Navigated"/> 里
        /// 同一帧启动，总时长 200ms ≤ 220ms（见 <see cref="PageTransition.PlayHostOverlapFade"/>）。
        /// 好处还顺带消掉了"拦下来的导航被新导航打断"那一整类边界情况
        /// （<c>_pendingSwap</c> / <c>_suppressLeaveFade</c> / <c>FlushPendingSwap</c> 全部不再需要）。
        /// </summary>
        private void RootFrame_Navigating(object sender, System.Windows.Navigation.NavigatingCancelEventArgs e)
        {
            if (e.Content is Page page)
            {
                page.Width = double.NaN;
                page.Height = double.NaN;
                page.HorizontalAlignment = HorizontalAlignment.Stretch;
                page.VerticalAlignment = VerticalAlignment.Stretch;
            }
        }

        #region 页面切换（宿主回落补偿 + 新页进场，两拍重叠 = 200ms）

        // 串行两拍的 _pendingSwap / _suppressLeaveFade / DeferPageSwap / CompletePendingSwap /
        // FlushPendingSwap 已全部删除：换页不再是"被拦下再重放"，而是立即完成，
        // 离场补偿（PageTransition.PlayHostOverlapFade）与新页进场同一帧启动。

        #endregion

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
             // 动效预热：把首次导航里的 JIT / 首次分配成本提前付掉，
             // 保证「第一次切页」的同步阻塞同样在 3ms 预算内（实测 3.1~3.6ms -> <3ms）。
             PageTransition.WarmUp();

             // Use the cached singleton so the home page keeps its state
             RootFrame.Navigate(GetCachedPage("home", () => new Views.HomePage()));
             RefreshNavigationVisibility();
             UpdateTopBarNavigation();
             ApplyInfoPanelState(true, animate: false);
             ApplyShellPanels(visible: true, animate: false);
             UpdateNavSlider(animate: false);
             // 被动刷新：目标没变时 UpdateNavSlider 内部会直接返回，不会打断正在跑的胶囊动画
             NavRailHost.SizeChanged += (s, e) => { if (!_navSliderAnimating) UpdateNavSlider(animate: false); };

             if (DataContext is ViewModels.MainViewModel vm && vm.BackgroundImage == null)
             {
                 try { await vm.LoadBackgroundAsync(); } catch { }
             }

             // Self-check: confirm the window really renders with the themed brushes
             Utilities.Logger.LogInfo(
                 $"[Theme] window root={(RootGrid.Background as SolidColorBrush)?.Color} " +
                 $"rail={(NavRailContainer.Background as SolidColorBrush)?.Color}");

             // 动效诊断的环境事实：DPI 缩放 / 渲染档位（BitmapCache 是否发虚、每帧成本都和它有关）
             try
             {
                 var dpi = VisualTreeHelper.GetDpi(this);
                 Utilities.Logger.LogInfo(
                     $"[MotionDbg] env dpi={dpi.DpiScaleX:0.##}x{dpi.DpiScaleY:0.##} " +
                     $"renderTier={System.Windows.Media.RenderCapability.Tier >> 16} " +
                     $"processRenderMode={System.Windows.Media.RenderOptions.ProcessRenderMode} " +
                     $"clientArea={ActualWidth:0.#}x{ActualHeight:0.#} animations={PageTransition.AnimationsEnabled}");
             }
             catch { }

        }

        private DateTime _lastActivatedRefresh = DateTime.MinValue;

        private async void MainWindow_Activated(object? sender, EventArgs e)
        {
            if (DateTime.Now - _lastActivatedRefresh < System.TimeSpan.FromSeconds(5)) return;
            _lastActivatedRefresh = DateTime.Now;

            if (DataContext is ViewModels.MainViewModel vm && !vm.IsLaunching)
            {
                await vm.LoadVersionsAsync();
                if (RootFrame.Content is Views.HomePage homePage)
                {
                    homePage.RefreshOverviewData();
                }
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F12)
            {
                NavigateTo("settings");
            }
        }

        private void OpenDownloadPage_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo("download");
        }

        #region Top bar (history / page identity / notifications / reload)

        private void RootFrame_Navigated(object sender, System.Windows.Navigation.NavigationEventArgs e)
        {
            // 导航**真正完成**后再刷新胶囊（放在 SyncNavRailSelection 里太早：
            // 那时 RootFrame.Content 还是旧页，拿到的是上一页的状态）。
            UpdatePillForCurrentPage();
            // 方向在 Navigating 之前就定好了：后退时 BackButton_Click 会先置位 _isNavigatingBack
            bool isForward = !_isNavigatingBack;
            _isNavigatingBack = false;

            SyncGlobalResourcesOverlay();
            UpdateTopBarNavigation();
            SyncInfoPanelPageContent();

            // ── Axolotl 页面切换（宿主淡出 + 新页进场，串行两拍）─────────────────────
            // 旧页离场：宿主容器 120ms ease（MainWindow.DeferPageSwap，不再抓位图快照）
            // 新页进入：global.scss .page-slide-enter-active { transition: opacity 0.18s ease }
            // 位移：global.scss .slide-enter-active { transform 0.2s ease }
            //       .slide-enter-from { translateY(30px) }，方向按前进 / 后退镜像。
            bool enterIsForward = isForward;

            if (RootFrame.Content is Page page)
            {
                // 「两拍重叠」：宿主回落补偿与新页位移/淡入在**同一帧**启动。
                // 总时长 = max(宿主 200ms, 新页 opacity 180ms, 新页 translateY 200ms) = 200ms ≤ 220ms。
                // 同步阻塞预算 3ms（任务要求），超出会打 OVER-BUDGET。
                using (MotionPerf.Measure("page-switch",
                    $"page={page.GetType().Name} dir={(enterIsForward ? "forward" : "back")}"))
                {
                    // ⚠ 顺序不能反：PlayPageEnter 开头的 SettleActiveMotion 会把动效集合里的
                    //   所有元素立即归位，而 PlayHostOverlapFade 会把宿主登记进那个集合 ——
                    //   先播宿主回落再播新页进场的话，宿主的 1->0.86->1 会在同一帧被清掉。
                    PageTransition.PlayPageEnter(page, enterIsForward);
                    PageTransition.PlayHostOverlapFade(PageTransitionHost);
                }

                // 全树残留普查：默认关闭（TSURU_MOTION_DBG=1 才做），日常操作不再有重型遍历
                MotionDiagnostics.ScheduleVerify(this);

                Utilities.Logger.LogInfo(
                    $"[MotionPerf] page-switch total={AxolotlMotion.PageEnterSlideMs:0}ms dir={(enterIsForward ? "forward" : "back")} " +
                    $"(was 200ms: opacity 180 + translateY 30, host dip cancelled) " +
                    $"host=opacity 1->{PageTransition.HostDipOpacity}->1 over " +
                    $"{PageTransition.HostOverlapMs / 2:0}+{PageTransition.HostOverlapMs / 2:0}ms cubic-bezier(0.4,0,0.2,1) started=after-enter " +
                    $"+ enter=opacity {AxolotlMotion.PageEnterFadeMs}ms ease, " +
                    $"translateY={(enterIsForward ? "+" : "-")}{AxolotlMotion.PageEnterSlidePx:0}px->0={AxolotlMotion.PageEnterSlideMs}ms cubic-bezier(0.22,1,0.36,1), " +
                    $"scale {AxolotlMotion.PageEnterScaleFrom}->1={AxolotlMotion.PageEnterScaleMs}ms same-curve " +
                    $"(overlapped) stagger={AxolotlMotion.StaggerStepMs}ms step/{AxolotlMotion.StaggerCapMs}ms cap/" +
                    $"{AxolotlMotion.StaggerItemMs}ms item/translateY({AxolotlMotion.StaggerOffsetPx}px) max={PageTransition.MaxStaggerItemCount} " +
                    $"sidebar={AxolotlMotion.SidebarWidthMs}ms animations={PageTransition.AnimationsEnabled} " +
                    $"motionDbg={MotionDiagnostics.Enabled}");
            }
        }

        /// <summary>
        /// 整页资源浮层只在「资源页 + 整页浏览态」下保持；导航到任何其它页面
        /// （左导航、顶栏前进/后退、--page、通知跳转）都会复位，保证左导航与右面板恢复显示。
        /// </summary>
        private void SyncGlobalResourcesOverlay()
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;

            bool keepOverlay = RootFrame.Content is Views.ResourcesPage page &&
                               page.DataContext is ViewModels.ResourcesViewModel resources &&
                               resources.IsFullPageView;

            vm.IsGlobalResourcesOverlayActive = keepOverlay;
        }

        /// <summary>Keeps the history arrows, page icon and page title in sync with the frame.</summary>
        private void UpdateTopBarNavigation()
        {
            BackButton.IsEnabled = RootFrame.CanGoBack;
            ForwardButton.IsEnabled = RootFrame.CanGoForward;

            var (title, icon) = DescribePage(RootFrame.Content);
            PageTitleText.Text = title;
            PageIcon.Symbol = icon;
        }

        private static (string Title, SymbolRegular Icon) DescribePage(object? content) => content switch
        {
            Views.HomePage => ("首页", SymbolRegular.Home24),
            Views.ResourcesPage => ("资源", SymbolRegular.Library24),
            Views.DownloadPage => ("下载", SymbolRegular.ArrowDownload24),
            Views.LabPage => ("实验室", SymbolRegular.Beaker24),
            Views.SettingsPage => ("设置", SymbolRegular.Settings24),
            Views.VersionSettingsPage => ("版本设置", SymbolRegular.Settings24),
            Views.VersionSelectorPage => ("选择版本", SymbolRegular.ArrowDownload24),
            Views.LoaderSelectionPage => ("选择加载器", SymbolRegular.ArrowDownload24),
            Views.DownloadManagerPage => ("下载管理", SymbolRegular.ArrowDownload24),
            Views.ResourceDetailPage => ("资源详情", SymbolRegular.Library24),
            _ => ("Tsuru Launcher", SymbolRegular.Home24)
        };

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (!RootFrame.CanGoBack) return;
            MotionDiagnostics.NavigationStarted("back");
            _isNavigatingBack = true;
            RootFrame.GoBack();
        }

        private void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (!RootFrame.CanGoForward) return;
            MotionDiagnostics.NavigationStarted("forward");
            RootFrame.GoForward();
        }

        private void NotificationsButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;

            int count = vm.NewsItems.Count;
            vm.NotificationService.Show(
                "通知中心",
                count > 0 ? $"有 {count} 条新动态，点这里回到首页查看。" : "暂时没有新的通知。",
                Services.NotificationType.Info,
                4,
                GoHomeFromNotification);
        }

        private void GoHomeFromNotification()
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(new Action(GoHomeFromNotification));
                return;
            }
            NavigateTo("home");
        }

        #endregion

        #region Right info panel

        // 收起 = 整列宽度过渡到 0。
        // 把手是**一个常显的半圆凸耳**（InfoPanelHandleButton，Axolotl .sidebar-toggle-handle 原形态）：
        // 平口贴住「本列的左缘」= 面板的左缘，圆弧朝面板外侧鼓出；
        // 列宽 268 <-> 0 的 320ms 过渡把这条边一路推到窗口右缘，于是收起态它自然变成
        // 「贴右缘、垂直居中」——展开 / 收起不需要两颗把手互换，凸耳全程可见可点、也不会跳位。
        // 另有一条 24px 的透明边缘热区（InfoPanelEdgeStrip）在收起态接住「鼠标推到底再点」的点击。
        // 列宽 = 面板 260px + ShellPanelMargin 右侧 8px 外边距；收起时整列收到 0。
        //
        // 改前是「10px 热区 + 鼠标进入才浮现 + 离开 200ms 后隐藏」：
        //   ① 热区贴的是客户区右缘，而窗口最外侧还有一圈 DWM 缩放边框（非客户区），
        //      用户把鼠标推到屏幕/窗口边缘时命中的是那圈边框 -> MouseEnter 根本不触发 -> 把手不出现；
        //   ② 即使侥幸浮现，200ms 延时隐藏也会在鼠标「正要按下去」的时候把它收走。
        //   两者合起来就是用户反馈的「很突兀、感觉不灵敏、点不开」。
        // 现在不再有任何延时隐藏：凸耳全程 Visible + IsHitTestVisible（整页浮层除外）。
        private const double InfoPanelWidthExpanded = 268;
        private const double InfoPanelWidthCollapsed = 0;

        /// <summary>
        /// 凸耳的 Z 序：高于页面宿主(1000) / 悬浮胶囊(900) / 边缘热区(1200)。
        /// 凸耳与边缘热区同属 BODY 那个 Grid，所以 ZIndex 是同一坐标系里比大小 ——
        /// 命中测试稳定落在凸耳上，不会被热区、页面宿主或悬浮胶囊盖住。
        /// </summary>
        private const int InfoPanelHandleZIndex = 1300;

        /// <summary>边缘热区的 Z 序：低于凸耳(1300)，高于页面宿主(1000) / 悬浮胶囊(900)。</summary>
        private const int InfoPanelEdgeStripZIndex = 1200;

        private bool _infoPanelExpanded = true;

        /// <summary>
        /// 点凸耳 = 展开 / 收起二态切换（Axolotl 的 .sidebar-toggle-handle 也是同一颗按钮切两态）。
        /// 只写状态：真正的宽度过渡 + 内容淡入淡出在 <see cref="ApplyInfoPanelState(bool)"/>。
        ///
        /// ⚠⚠ 之前在这里有 <c>if (IsGlobalOverlayActive) return;</c> —— 用户在「资源页 → 展开全部」状态下
        /// 点右侧凸耳，**整颗按钮变无效**（无法收起右栏）。问题不是凸耳被遮挡，是这里早退。
        /// 实际上凸耳在整页态下仍然可见且被期望可点（参考用户的截图 3），所以不要在这里拦截。
        /// </summary>
        private void InfoPanelHandle_Click(object sender, RoutedEventArgs e)
        {
            ApplyInfoPanelState(!_infoPanelExpanded);
        }

        /// <summary>自检用：外部触发一次「信息面板」折叠 / 展开。</summary>
        public void ToggleInfoPanelForSelfTest() => ApplyInfoPanelState(!_infoPanelExpanded);

        /// <summary>自检用：右信息面板那一列的实际宽度（动画结束后）。用于定位「两边都收起还有空缺一大块」这类问题。</summary>
        public double InfoPanelColumnWidth => InfoPanelColumn?.ActualWidth ?? -1;

        /// <summary>
        /// 收起态右边缘 24px 热区被点击 —— 和点凸耳等价（这条热区只会把面板**展开**，
        /// 因为展开后它就被 SyncInfoPanelEdge 关掉了，否则一整条会挡住面板内容）。
        /// </summary>
        private void InfoPanelEdge_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => InfoPanelExpand_Click(sender, e);

        private void InfoPanelExpand_Click(object sender, RoutedEventArgs e)
        {
            if (IsGlobalOverlayActive) return;
            ApplyInfoPanelState(true);
        }

        /// <summary>
        /// 整页资源浮层开关：左导航栏 / 右侧信息面板不是「Visibility 硬切」，
        /// 隐藏时先播 120ms ease 淡出（Axolotl .page-slide-leave）再 Collapsed，
        /// 显示时先 Visible 再 180ms ease 淡入（.page-slide-enter）。
        /// </summary>
        private void ApplyShellPanels(bool visible, bool animate)
        {
            FadeShellPanel(NavRailContainer, visible, animate);

            // 右信息面板在「资源」页承载筛选卡片（Controls/ResourcesFilterPanel）。
            // 资源页一搜索/筛选就会进整页浏览态，如果连右栏一起藏掉，用户点一下筛选它自己就消失了 ——
            // 所以资源页上右栏始终保留，整页态只是把左导航栏让出去。
            FadeShellPanel(InfoPanelHost, visible || InfoPanelKeptInOverlay, animate);
        }

        /// <summary>「资源」页时右信息面板要在整页浏览态下保留（它承载筛选卡片）。</summary>
        private bool InfoPanelKeptInOverlay => RootFrame?.Content is Views.ResourcesPage;

        private void FadeShellPanel(FrameworkElement? panel, bool visible, bool animate)
        {
            if (panel == null) return;

            if (!animate || !PageTransition.AnimationsEnabled)
            {
                panel.BeginAnimation(UIElement.OpacityProperty, null);
                panel.Opacity = visible ? 1 : 0;
                panel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                return;
            }

            if (visible) panel.Visibility = Visibility.Visible;

            // 先清掉上一轮还没播完的时钟：连点「展开全部」时不会出现两个时钟抢同一个属性
            panel.BeginAnimation(UIElement.OpacityProperty, null);
            if (!visible) panel.Opacity = 1.0;

            double to = visible ? 1.0 : 0.0;
            double ms = visible ? AxolotlMotion.PageEnterMs : AxolotlMotion.PageLeaveMs;
            var ease = visible ? AxolotlMotion.Ease : AxolotlMotion.EaseIn;
            var anim = new DoubleAnimation(to, AxolotlMotion.Ms(ms)) { EasingFunction = ease };
            anim.Completed += (s, e) =>
            {
                if (visible) return;
                panel.BeginAnimation(UIElement.OpacityProperty, null);
                panel.Opacity = 0;
                panel.Visibility = Visibility.Collapsed;
            };
            panel.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        /// <summary>整页资源浮层里不显示右侧面板的任何入口。</summary>
        private bool IsGlobalOverlayActive
            => DataContext is ViewModels.MainViewModel vm && vm.IsGlobalResourcesOverlayActive;

        /// <summary>
        /// 把手 / 边缘热区的可见性、Z 序与箭头方向在这里显式同步（用户要求用
        /// Panel.ZIndex + IsHitTestVisible 双重校验，另见 <see cref="VerifyInfoPanelHandleOnTop"/>）。
        ///
        /// * 凸耳：**常显可点**（展开、收起两种状态都在，这正是 Axolotl 的形态 ——
        ///   .sidebar-toggle-handle 一直挂在 sidebar 左缘）。只有整页资源浮层把整条壳层盖掉时才让位。
        /// * 箭头：展开 -> ChevronRight24（点它把面板收到右边去），收起 -> ChevronLeft24（点它拉出来）。
        /// * 边缘热区：只在收起态可见可点。展开时右缘就是面板本体，一整条热区还开着会挡住面板内容。
        /// </summary>
        private void SyncInfoPanelEdge()
        {
            // 资源页上右栏在整页浏览态下也保留（承载筛选卡片），所以凸耳 / 边缘热区不能跟着收掉。
            bool overlay = IsGlobalOverlayActive && !InfoPanelKeptInOverlay;

            Panel.SetZIndex(InfoPanelHandleButton, InfoPanelHandleZIndex);
            InfoPanelHandleButton.IsHitTestVisible = !overlay;
            InfoPanelHandleButton.Visibility = overlay ? Visibility.Collapsed : Visibility.Visible;

            InfoPanelHandleIcon.Symbol = _infoPanelExpanded
                ? SymbolRegular.ChevronRight24
                : SymbolRegular.ChevronLeft24;

            Panel.SetZIndex(InfoPanelEdgeStrip, InfoPanelEdgeStripZIndex);
            bool edgeActive = !_infoPanelExpanded && !overlay;
            InfoPanelEdgeStrip.IsHitTestVisible = edgeActive;
            InfoPanelEdgeStrip.Visibility = edgeActive ? Visibility.Visible : Visibility.Collapsed;

            VerifyInfoPanelHandleOnTop();
        }

        /// <summary>
        /// 凸耳「不被其他元素盖住」的**实测**校验：在凸耳可见区域的正中心做一次
        /// <see cref="VisualTreeHelper.HitTest(Visual, Point)"/>，确认最上层命中的是凸耳自己
        /// （或它的子元素，例如箭头图标）。结果写进日志，验收时可直接 grep。
        ///
        /// 用 Background 优先级投递：调用点可能还在列宽动画中间（布局每帧都在变），
        /// 而且它绝不占用点击回调的同步预算（硬性要求 &lt; 3ms）。
        /// </summary>
        private void VerifyInfoPanelHandleOnTop(bool settled = false)
        {
            if (RootGrid == null || InfoPanelHandleButton == null) return;

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    if (!InfoPanelHandleButton.IsVisible)
                    {
                        Utilities.Logger.LogInfo(
                            $"[Motion] sidebar-handle hit-test skipped visible=false " +
                            $"hitTestVisible={InfoPanelHandleButton.IsHitTestVisible} " +
                            $"zIndex={Panel.GetZIndex(InfoPanelHandleButton)}");
                        return;
                    }

                    // 命中点取可见凸耳（模板里的 Lug）的中心，而不是按钮含不可见 padding 的中心：
                    // 用户点得到的那块地方就是凸耳本身。
                    var lug = InfoPanelHandleButton.Template?.FindName("Lug", InfoPanelHandleButton)
                                  as FrameworkElement ?? InfoPanelHandleButton;

                    var probe = lug.TranslatePoint(
                        new Point(Math.Max(1.0, lug.ActualWidth / 2.0), Math.Max(1.0, lug.ActualHeight / 2.0)),
                        RootGrid);

                    var hit = VisualTreeHelper.HitTest(RootGrid, probe)?.VisualHit as DependencyObject;
                    bool onTop = hit != null &&
                                 (ReferenceEquals(hit, InfoPanelHandleButton) || IsInVisualSubtree(hit, InfoPanelHandleButton));

                    Utilities.Logger.LogInfo(
                        $"[Motion] sidebar-handle hit-test point=({probe.X:0.#},{probe.Y:0.#}) " +
                        $"top={DescribeVisual(hit)} ok={onTop} zIndex={Panel.GetZIndex(InfoPanelHandleButton)} " +
                        $"edgeStripZ={Panel.GetZIndex(InfoPanelEdgeStrip)} " +
                        $"hitTestVisible={InfoPanelHandleButton.IsHitTestVisible} " +
                        $"visible={InfoPanelHandleButton.IsVisible} " +
                        $"lug={lug.ActualWidth:0.#}x{lug.ActualHeight:0.#} " +
                        $"button={InfoPanelHandleButton.ActualWidth:0.#}x{InfoPanelHandleButton.ActualHeight:0.#} " +
                        $"expanded={_infoPanelExpanded} settled={settled}");
                }
                catch (Exception ex)
                {
                    Utilities.Logger.LogError(ex, "VerifyInfoPanelHandleOnTop");
                }
            }));
        }

        /// <summary>node 是否落在 root 的可视子树里（HitTest 命中的可能是模板里的箭头图标）。</summary>
        private static bool IsInVisualSubtree(DependencyObject? node, DependencyObject root)
        {
            for (var cur = node; cur != null; cur = VisualTreeHelper.GetParent(cur))
            {
                if (ReferenceEquals(cur, root)) return true;
            }
            return false;
        }

        private static string DescribeVisual(DependencyObject? node)
            => node switch
            {
                null => "<null>",
                FrameworkElement fe => string.IsNullOrEmpty(fe.Name) ? fe.GetType().Name : fe.GetType().Name + "#" + fe.Name,
                _ => node.GetType().Name,
            };

        /// <summary>
        /// 展开 = 260px 面板（左缘圆形折角按钮收起）；收起 = 列宽过渡到 0。
        ///
        /// Axolotl App.vue:3376-3380 用
        /// <c>transition: --right-bar-width 320ms cubic-bezier(0.22, 1, 0.36, 1)</c>
        /// 直接过渡 grid-template-columns 上的右栏宽度，所以这里动画 Grid 列宽，
        /// 面板本身保持固定 260px 宽并被裁切 —— 不是瞬时切宽度，也不会把内容压扁。
        /// </summary>
        private void ApplyInfoPanelState(bool expanded)
            => ApplyInfoPanelState(expanded, animate: true);

        private void ApplyInfoPanelState(bool expanded, bool animate)
        {
            bool changed = _infoPanelExpanded != expanded;
            _infoPanelExpanded = expanded;

            double target = expanded ? InfoPanelWidthExpanded : InfoPanelWidthCollapsed;

            // 无状态变化 / 调用方要求不播：直接把两样东西都落到终态（列宽 + 内容不透明度），
            // 一点动画时钟都不留。
            if (!animate || !changed)
            {
                InfoPanelColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
                InfoPanelColumn.Width = new GridLength(target);
                PageTransition.SettleElement(InfoPanelSurface);
                // 收起态的内容不透明度必须是 0：不能走 SettleElement（那个会把 Opacity 复位成 1）
                PageTransition.SettleOpacity(InfoPanelContent, expanded ? 1.0 : 0.0);
                SyncInfoPanelEdge();
                return;
            }

            // ── 两件事同时起跑，都只动 Width / Opacity（都不触发布局重排以外的属性）──
            //   ① 列宽 268 <-> 0：320ms cubic-bezier(0.22, 1, 0.36, 1)（Axolotl --right-bar-width 原值）
            //   ② 面板内容 150ms 淡入 / 淡出（Tailwind transition 默认 150ms）
            using (MotionPerf.Measure("sidebar-toggle", $"{(expanded ? "expand" : "collapse")} -> {target:0}px"))
            {
                PageTransition.AnimateSidebarWidth(InfoPanelColumn, InfoPanelSurface, target, onCompleted: () =>
                {
                    // 结束强制归位：清时钟 + 列宽写死 + 摘掉宽度动画期间挂的 BitmapCache。
                    // （AnimateSidebarWidth 自己也会做一遍，这里再兜一次，任何路径都不会停在半宽。）
                    InfoPanelColumn.BeginAnimation(ColumnDefinition.WidthProperty, null);
                    InfoPanelColumn.Width = new GridLength(target);
                    PageTransition.SettleElement(InfoPanelSurface);

                    // 过渡跑完后再做一次命中测试：SyncInfoPanelEdge 里那次是动画刚起步时投递的，
                    // 凸耳可能还在半路上（日志里会看到 expanded=True 但坐标还是收起态的位置）。
                    // 这一次拿到的是**定格后**的几何：展开 -> (900,408) 面板左缘，收起 -> (1168,408) 窗口右缘。
                    VerifyInfoPanelHandleOnTop(settled: true);
                });

                PageTransition.PlayOpacity(InfoPanelContent, expanded ? 1.0 : 0.0,
                    AxolotlMotion.SidebarTextFadeMs,
                    expanded ? AxolotlMotion.Ease : AxolotlMotion.EaseIn);
            }

            Utilities.Logger.LogInfo(
                $"[MotionPerf] sidebar column {InfoPanelColumn.ActualWidth:0.#}px -> {target}px " +
                $"over {AxolotlMotion.SidebarWidthMs}ms cubic-bezier(0.22, 1, 0.36, 1) " +
                $"surface=260px left-aligned slide-out (面板整体跟着列的左缘向右滑出去，被 ClipToBounds 裁掉；" +
                $"宽度恒定，绝不压扁/重排内容) + content opacity -> {(expanded ? 1 : 0)} " +
                $"over {AxolotlMotion.SidebarTextFadeMs}ms ease " +
                $"(animations={PageTransition.AnimationsEnabled})");

            // 右栏内容跟着列宽一起「展开」：四段（账号 / 洞察 / 每日挑战 / 新闻）
            // 按 40ms 步长错峰淡入，列宽动画把内容滑出来的时候不会是死板的一整块。
            if (expanded)
            {
                // 错峰目标跟着当前内容走：资源页只有筛选卡片一棵，首页/其它页才是四段。
                var staggerHost = InfoPanelHomeSections.Visibility == Visibility.Visible
                    ? (Panel)InfoPanelHomeSections
                    : InfoPanelContent;
                PageTransition.PlayStaggeredIn(staggerHost);
                Utilities.Logger.LogInfo(
                    $"[Motion] sidebar content stagger step={AxolotlMotion.StaggerStepMs}ms " +
                    $"cap={AxolotlMotion.StaggerCapMs}ms item={AxolotlMotion.StaggerItemMs}ms " +
                    $"translateY({AxolotlMotion.StaggerOffsetPx}px) (animations={PageTransition.AnimationsEnabled})");
            }

            SyncInfoPanelEdge();
        }

        // ─────────── 账号面板（右栏内原地展开，不弹到页面上） ───────────
        //
        // 「当前游玩账号」那张卡现在是可点的按钮：点它在右栏内部原地展开账号面板
        // （账号列表：头像 + 用户名 + 离线/正版/外置标签 + 「当前」标记 + 复制 / 删除，
        //   三行「+ 添加 Microsoft 账户 / 第三方账号 / 离线账户」），全部动作落到 VM 现成的
        // IsAccountFlyoutOpen / SelectAccount / RemoveAccount / LoginMicrosoftAsync /
        // LoginYggdrasilAsync / LoginOffline 那一套（AccountManager 是唯一真源，这里不改 VM）。
        //
        // 动效（三样都在回调里强制归位；中途连点只会从当前实际高度继续，不会跳变）：
        //   * chevron    收起朝下(0°) <-> 展开朝上(180°)，150ms 旋转（只动 RenderTransform）
        //   * 面板高度   0 <-> 内容自然高度，200ms cubic-bezier(0.22, 1, 0.36, 1)（AxolotlMotion.SidebarEase）
        //   * 内容       150ms 淡入 / 淡出（PageTransition.PlayOpacity，自带终态兜底）
        // 只动 Height / RenderTransform / Opacity —— 点击路径上没有同步 IO，也没有整体布局重建。
        private const double AccountPanelMs = 200;
        private const double AccountChevronMs = 150;

        /// <summary>账号面板是否展开（界面状态与它一一对应）。</summary>
        private bool _accountPanelExpanded;

        /// <summary>高度动画的兜底归位定时器：Completed 因故没来也不会停在半高。</summary>
        private DispatcherTimer? _accountPanelSettleTimer;

        private void InfoAccountCard_Click(object sender, RoutedEventArgs e)
            => ApplyAccountPanel(!_accountPanelExpanded, animate: true);

        /// <summary>
        /// 展开 / 收起右栏内的账号面板。展开时高度从 0 过渡到内容的自然高度
        /// （先量一次 AccountPanelInner.DesiredSize，量不出来就直接到位，绝不卡在半高）。
        /// </summary>
        private void ApplyAccountPanel(bool expanded, bool animate)
        {
            if (AccountPanelClip == null || AccountPanelInner == null) return;

            bool changed = _accountPanelExpanded != expanded;
            _accountPanelExpanded = expanded;

            AnimateAccountChevron(expanded, animate);

            // 上一轮可能还挂着的兜底定时器先停掉（连点时不会有两个时钟抢同一个属性）
            _accountPanelSettleTimer?.Stop();
            _accountPanelSettleTimer = null;

            if (!animate || !changed || !PageTransition.AnimationsEnabled)
            {
                SettleAccountPanel(expanded);
                return;
            }

            double from = AccountPanelClip.Height;
            if (double.IsNaN(from)) from = AccountPanelClip.ActualHeight;
            if (from < 0) from = 0;

            double target;
            if (expanded)
            {
                AccountPanelClip.Visibility = Visibility.Visible;
                target = MeasureAccountPanelHeight();
                if (double.IsNaN(target)) { SettleAccountPanel(true); return; }
            }
            else
            {
                if (AccountPanelClip.Visibility != Visibility.Visible) { SettleAccountPanel(false); return; }
                target = 0;
            }

            using (MotionPerf.Measure("account-panel",
                $"{(expanded ? "expand" : "collapse")} {from:0.#}px -> {target:0.#}px"))
            {
                AccountPanelClip.BeginAnimation(FrameworkElement.HeightProperty, null);
                AccountPanelClip.Height = from;

                var anim = new DoubleAnimation(from, target, AxolotlMotion.Ms(AccountPanelMs))
                {
                    EasingFunction = AxolotlMotion.SidebarEase,
                };
                anim.Completed += (s, e) => SettleAccountPanel(_accountPanelExpanded);
                AccountPanelClip.BeginAnimation(FrameworkElement.HeightProperty, anim);

                // 兜底：动画时钟被系统掐掉时也要落到终态（动画结束强制归位）
                _accountPanelSettleTimer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(AccountPanelMs + 120),
                };
                _accountPanelSettleTimer.Tick += (s, e) => SettleAccountPanel(_accountPanelExpanded);
                _accountPanelSettleTimer.Start();

                // 内容 150ms 淡入 / 淡出（Tailwind transition 默认 150ms，与右栏整体折叠同一档）
                PageTransition.PlayOpacity(AccountPanelInner, expanded ? 1.0 : 0.0,
                    AxolotlMotion.SidebarTextFadeMs,
                    expanded ? AxolotlMotion.Ease : AxolotlMotion.EaseIn);
            }

            Utilities.Logger.LogInfo(
                $"[MotionPerf] account-panel {(expanded ? "expand" : "collapse")} " +
                $"height {from:0.#}px -> {target:0.#}px over {AccountPanelMs}ms cubic-bezier(0.22, 1, 0.36, 1) " +
                $"+ content opacity -> {(expanded ? 1 : 0)} over {AxolotlMotion.SidebarTextFadeMs}ms ease " +
                $"+ chevron {(expanded ? "180" : "0")}deg over {AccountChevronMs}ms " +
                $"(animations={PageTransition.AnimationsEnabled}, in-panel 原地展开)");
        }

        /// <summary>
        /// 强制归位：清掉高度时钟 + 写死终态（展开 = 自然高度 auto，收起 = 0 + Collapsed），
        /// 内容不透明度同样落到终态。任何路径（Completed / 兜底定时器 / 无动效 / 打断）都走这里。
        /// </summary>
        private void SettleAccountPanel(bool expanded)
        {
            if (AccountPanelClip == null || AccountPanelInner == null) return;
            try
            {
                _accountPanelSettleTimer?.Stop();
                _accountPanelSettleTimer = null;

                AccountPanelClip.BeginAnimation(FrameworkElement.HeightProperty, null);
                AccountPanelClip.Opacity = 1.0;
                if (expanded)
                {
                    AccountPanelClip.Visibility = Visibility.Visible;
                    AccountPanelClip.Height = double.NaN;   // 自然高度：账号增减后不会被写死
                    PageTransition.SettleOpacity(AccountPanelInner, 1.0);
                }
                else
                {
                    AccountPanelClip.Height = 0;
                    AccountPanelClip.Visibility = Visibility.Collapsed;
                    PageTransition.SettleOpacity(AccountPanelInner, 0.0);
                }
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "SettleAccountPanel");
            }
        }

        /// <summary>
        /// 量一次账号面板内容的自然高度（用于展开动画的目标值）。
        /// 面板首次展开时它自己还没被量过（Height=0 / Collapsed），就用面板固定宽度推算，
        /// 量不出来返回 NaN，调用方直接到位。
        /// </summary>
        private double MeasureAccountPanelHeight()
        {
            try
            {
                // 用内容区实际宽度量（和布局走同一条 Measure 路径，展开动画的终点 = 布局的自然高度，
                // 不会在归位时跳一下）。首次展开时容器还是 Collapsed（ActualWidth=0），
                // 就用面板固定宽度推算：260 面板 - 左缘折角列(24+8) - 内容左右外边距(2+12) - 描边。
                double width = InfoPanelContent != null && InfoPanelContent.ActualWidth > 1
                    ? InfoPanelContent.ActualWidth
                    : Math.Max(140, InfoPanelSurface.Width - 48);

                // 有显式 Height 时 DesiredSize 就是那个 Height，所以量之前先把高度摘掉，量完还回去。
                double restore = AccountPanelClip.Height;
                AccountPanelClip.Height = double.NaN;
                AccountPanelClip.Measure(new Size(width, double.PositiveInfinity));
                double h = AccountPanelClip.DesiredSize.Height;
                AccountPanelClip.Height = restore;
                return h > 1 ? h : double.NaN;
            }
            catch
            {
                return double.NaN;
            }
        }

        /// <summary>卡片右侧 chevron：收起朝下(0°) / 展开朝上(180°)，150ms，只动 RenderTransform。</summary>
        private void AnimateAccountChevron(bool expanded, bool animate)
        {
            if (AccountCardChevronRotate == null) return;

            double to = expanded ? 180.0 : 0.0;
            AccountCardChevronRotate.BeginAnimation(RotateTransform.AngleProperty, null);

            if (!animate || !PageTransition.AnimationsEnabled)
            {
                AccountCardChevronRotate.Angle = to;
                return;
            }

            double from = AccountCardChevronRotate.Angle;
            if (Math.Abs(from - to) < 0.5)
            {
                AccountCardChevronRotate.Angle = to;
                return;
            }

            var anim = new DoubleAnimation(from, to, AxolotlMotion.Ms(AccountChevronMs))
            {
                EasingFunction = AxolotlMotion.EaseInOut,
            };
            anim.Completed += (s, e) =>
            {
                AccountCardChevronRotate.BeginAnimation(RotateTransform.AngleProperty, null);
                AccountCardChevronRotate.Angle = to;
            };
            AccountCardChevronRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
        }

        /// <summary>点账号行 = 切成当前账号（写配置走 VM 的合并写盘，不在点击路径上落盘）。</summary>
        private void AccountRow_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not Models.Account acc) return;
            if (DataContext is not ViewModels.MainViewModel vm) return;

            vm.SelectAccount(acc);
        }

        private void CopyAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not Models.Account acc) return;
            if (DataContext is not ViewModels.MainViewModel vm) return;

            try
            {
                Clipboard.SetText(acc.Username ?? string.Empty);
                vm.NotificationService.ShowSuccess("已复制", $"用户名 {acc.Username} 已复制到剪贴板");
            }
            catch (Exception ex)
            {
                iOS26Dialog.Show($"复制失败: {ex.Message}", "错误", DialogIcon.Error);
            }
        }

        /// <summary>删除账号（当前账号被删掉时 AccountManager 会自动落到下一个账号）。</summary>
        private void DeleteAccount_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not Models.Account acc) return;
            if (DataContext is not ViewModels.MainViewModel vm) return;

            vm.RemoveAccount(acc);
            vm.NotificationService.Show("已删除账号", acc.Username ?? string.Empty, Services.NotificationType.Info);
        }

        private async void AddMicrosoftAccount_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;
            Utilities.Logger.LogInfo("[Account] 右栏账号面板 -> 添加 Microsoft 账户（打开微软登录窗口）");

            try
            {
                // 登录流程会自己开微软登录窗口（MicrosoftLoginDialog）；
                // 用户直接关掉窗口 = 取消，不是错误，不弹错误框。
                await vm.LoginMicrosoftAsync();
            }
            catch (OperationCanceledException)
            {
                Utilities.Logger.LogInfo("[Account] Microsoft 登录已取消（用户关闭登录窗口）");
            }
            catch (Exception ex)
            {
                iOS26Dialog.Show($"登录失败: {ex.Message}", "错误", DialogIcon.Error);
            }
        }

        private async void AddYggdrasilAccount_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;
            Utilities.Logger.LogInfo("[Account] 右栏账号面板 -> 添加第三方账号（外置登录，弹出输入框）");

            var fields = new[]
            {
                new AccountPromptField("服务器地址（API Root）", "https://littleskin.cn/api/yggdrasil", false),
                new AccountPromptField("邮箱 / 用户名", string.Empty, false),
                new AccountPromptField("密码", string.Empty, true),
            };
            if (!ShowAccountPrompt("添加第三方账号（外置登录）", fields, out var values)) return;

            try
            {
                await vm.LoginYggdrasilAsync(values[0], values[1], values[2]);
            }
            catch (Exception ex)
            {
                iOS26Dialog.Show($"外置登录失败: {ex.Message}", "错误", DialogIcon.Error);
            }
        }

        private void AddOfflineAccount_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;
            Utilities.Logger.LogInfo("[Account] 右栏账号面板 -> 添加离线账户（弹出输入框）");

            var fields = new[] { new AccountPromptField("玩家名", "Player", false) };
            if (!ShowAccountPrompt("添加离线账户", fields, out var values)) return;

            vm.LoginOffline(values[0]);
        }

        /// <summary>账号面板里的一行输入框描述。</summary>
        private readonly record struct AccountPromptField(string Label, string Initial, bool IsPassword);

        /// <summary>
        /// 极简输入对话框（离线账号 1 个字段 / 外置登录 3 个字段）。
        /// 与首页账号浮层用的是同一套形态；颜色全部走 <see cref="Utilities.ThemeBrush"/>
        /// （跟随主题与强调色，不写死色值）。
        /// </summary>
        private bool ShowAccountPrompt(string title, AccountPromptField[] fields, out string[] values)
        {
            values = Array.Empty<string>();
            Utilities.Logger.LogInfo($"[Account] 输入对话框 title={title} fields={fields.Length}");
            if (fields == null || fields.Length == 0) return false;

            var card = (Application.Current?.TryFindResource("SurfaceFloatingBrush") as SolidColorBrush)
                       ?? new SolidColorBrush(Color.FromRgb(0x1A, 0x22, 0x1E));
            var inputBg = (Application.Current?.TryFindResource("SurfaceInputBrush") as SolidColorBrush)
                          ?? new SolidColorBrush(Color.FromRgb(0x0C, 0x11, 0x0F));
            var border = (Application.Current?.TryFindResource("GlassBorderBrush") as SolidColorBrush)
                         ?? new SolidColorBrush(Colors.Gray);
            var text = Utilities.ThemeBrush.TextPrimary;

            var dlg = new Window
            {
                Title = title,
                SizeToContent = SizeToContent.Height,
                Width = 400,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = card,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.SingleBorderWindow,
            };
            System.Windows.Automation.AutomationProperties.SetName(dlg, title);

            var root = new StackPanel { Margin = new Thickness(22) };
            var inputs = new List<System.Windows.Controls.Control>();

            foreach (var f in fields)
            {
                root.Children.Add(new System.Windows.Controls.TextBlock
                {
                    Text = f.Label,
                    Foreground = Utilities.ThemeBrush.TextTertiary,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 0, 6),
                });

                System.Windows.Controls.Control input;
                if (f.IsPassword)
                {
                    input = new System.Windows.Controls.PasswordBox { Password = f.Initial, FontSize = 13, Padding = new Thickness(10, 7, 10, 7), Foreground = text, BorderBrush = border, Background = inputBg };
                }
                else
                {
                    input = new System.Windows.Controls.TextBox { Text = f.Initial, FontSize = 13, Padding = new Thickness(10, 7, 10, 7), Foreground = text, BorderBrush = border, Background = inputBg, CaretBrush = Utilities.ThemeBrush.Accent };
                }

                input.Margin = new Thickness(0, 0, 0, 14);
                inputs.Add(input);
                root.Children.Add(input);
            }

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new System.Windows.Controls.Button { Content = "取消", Padding = new Thickness(20, 7, 20, 7), Margin = new Thickness(0, 0, 8, 0), Cursor = System.Windows.Input.Cursors.Hand };
            var ok = new System.Windows.Controls.Button
            {
                Content = "确定",
                Padding = new Thickness(22, 7, 22, 7),
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = Utilities.ThemeBrush.Accent,
                Foreground = Utilities.ThemeBrush.AccentForeground,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
            };
            cancel.Click += (_, __) => { dlg.DialogResult = false; };
            ok.Click += (_, __) => { dlg.DialogResult = true; };
            buttons.Children.Add(cancel);
            buttons.Children.Add(ok);
            root.Children.Add(buttons);

            dlg.Content = root;
            dlg.Loaded += (_, __) => { if (inputs.Count > 0) inputs[0].Focus(); };

            if (dlg.ShowDialog() != true) return false;

            values = new string[inputs.Count];
            for (int i = 0; i < inputs.Count; i++)
            {
                values[i] = inputs[i] switch
                {
                    System.Windows.Controls.PasswordBox pb => pb.Password ?? string.Empty,
                    System.Windows.Controls.TextBox tb => tb.Text ?? string.Empty,
                    _ => string.Empty,
                };
            }
            return true;
        }

        #endregion

        #region Floating pill (list / grid / pencil)

        /// <summary>
        /// 📋 列表按钮：极简主页。
        ///
        /// 这里只写 ViewModel；真正的两态切换（含动画）由 <c>HomePage.ApplyHomeMode</c> 负责，
        /// 并且它自带"HomePage 不是当前页 / 不在可视树 → 只切模式不播动画"的防御
        /// （左导航在设置页时点 ▦/📋 不会再去动一个不可见的页面）。
        /// </summary>
        /// <summary>
        /// 悬浮胶囊「列表 / 网格」按**当前页**决定行为（之前只在首页生效，
        /// 在资源页点它动的是首页的视图模式 —— 用户报的「切换视图只在首页有用」就是这个）：
        ///   * 首页：列表 = 极简主页，网格 = 信息主页
        ///   * 资源页：列表 / 网格 = 搜索结果的两态（<see cref="ViewModels.ResourcesViewModel.IsListView"/>）
        ///   * 其他页：这两个按钮没有对应语义，点一下回首页（并给出提示）
        /// </summary>
        private void HomeListMode_Click(object sender, RoutedEventArgs e) => ApplyPillViewMode(list: true);

        /// <summary>▦ 网格按钮。</summary>
        private void HomeGridMode_Click(object sender, RoutedEventArgs e) => ApplyPillViewMode(list: false);

        private void ApplyPillViewMode(bool list)
        {
            try
            {
                if (RootFrame?.Content is Views.HomePage)
                {
                    if (DataContext is ViewModels.MainViewModel vm)
                    {
                        using (MotionPerf.Measure("home-mode-click", list ? "target=minimal" : "target=dashboard"))
                        {
                            vm.IsMinimalHome = list;
                        }
                        MeasureHomeModeFirstFrame(list ? "target=minimal" : "target=dashboard");
                    }
                    UpdatePillForCurrentPage();
                    return;
                }

                if (RootFrame?.Content is Views.ResourcesPage rp &&
                    rp.DataContext is ViewModels.ResourcesViewModel rvm)
                {
                    using (MotionPerf.Measure("pill-viewmode-click", list ? "target=list" : "target=grid"))
                    {
                        rvm.IsListView = list;
                    }
                    UpdatePillForCurrentPage();
                    return;
                }

                // 其他页面：回首页再用（并让用户知道为什么）
                if (DataContext is ViewModels.MainViewModel mvm)
                    mvm.NotificationService.ShowSuccess("视图切换", "这个切换只对首页和资源页有意义，已带你回到首页。");
                NavigateTo("home");
            }
            catch (Exception ex) { Utilities.Logger.LogError(ex, "ApplyPillViewMode"); }
        }

        /// <summary>
        /// 按当前页刷新胶囊的选中态与「编辑」按钮的可见性。
        /// 每次导航后都要调 —— 否则从资源页回到首页时，胶囊还停在资源页的选中态。
        /// </summary>
        private void UpdatePillForCurrentPage()
        {
            try
            {
                bool? listChecked = null;

                if (RootFrame?.Content is Views.HomePage)
                {
                    if (DataContext is ViewModels.MainViewModel vm) listChecked = vm.IsMinimalHome;
                }
                else if (RootFrame?.Content is Views.ResourcesPage rp &&
                         rp.DataContext is ViewModels.ResourcesViewModel rvm)
                {
                    listChecked = rvm.IsListView;
                }

                if (listChecked.HasValue)
                {
                    if (HomeListModeButton.IsChecked != listChecked.Value)
                        HomeListModeButton.IsChecked = listChecked.Value;
                    if (HomeGridModeButton.IsChecked == listChecked.Value)
                        HomeGridModeButton.IsChecked = !listChecked.Value;
                }

                // 整条胶囊**只在首页出现** —— 它整条都是「首页视图模式（极简/信息）+ 编辑小组件」，
                // 放到资源页 / 实验室 / 设置上没有任何对应语义，反而让人以为能切换当前页视图。
                // （2026-09-21 用户明确要求：这个只在首页出现。）
                bool onHome = RootFrame?.Content is Views.HomePage;
                FloatingPill.Visibility = onHome ? Visibility.Visible : Visibility.Collapsed;

                // 「编辑小组件」只对**网格视图（信息主页）**有意义 —— 极简主页没有小组件网格，
                // 所以在极简视图下把铅笔按钮藏掉（用户明确要求：添加小组件只适用于网格视图）。
                bool gridHome = onHome && DataContext is ViewModels.MainViewModel m && !m.IsMinimalHome;
                HomeEditButton.Visibility = gridHome ? Visibility.Visible : Visibility.Collapsed;

                // 编辑态下整条胶囊让位给仪表盘底部那条「小组件工具条」（Axolotl 同做法），
                // 否则两个都贴在右下角会重叠。
                if (DataContext is ViewModels.MainViewModel em && em.IsHomeEditing)
                    FloatingPill.Visibility = Visibility.Collapsed;

                string pillRect = "-", editRect = "-";
                try
                {
                    if (FloatingPill.IsVisible && FloatingPill.ActualWidth > 0)
                    {
                        var tl = FloatingPill.PointToScreen(new Point(0, 0));
                        pillRect = (int)tl.X + "," + (int)tl.Y + " " + (int)FloatingPill.ActualWidth + "x" + (int)FloatingPill.ActualHeight;
                    }
                    if (HomeEditButton.IsVisible && HomeEditButton.ActualWidth > 0)
                    {
                        var t2 = HomeEditButton.PointToScreen(new Point(0, 0));
                        editRect = (int)t2.X + "," + (int)t2.Y + " " + (int)HomeEditButton.ActualWidth + "x" + (int)HomeEditButton.ActualHeight;
                    }
                }
                catch { }

                Utilities.Logger.LogInfo("[PillDiag] page=" + (RootFrame?.Content?.GetType().Name ?? "-") +
                    " pill=" + FloatingPill.Visibility +
                    " editBtn=" + HomeEditButton.Visibility +
                    " pillRect=" + pillRect + " editRect=" + editRect +
                    " listChecked=" + (listChecked?.ToString() ?? "-"));
            }
            catch (Exception ex) { Utilities.Logger.LogError(ex, "UpdatePillForCurrentPage"); }
        }

        // ─────────── R1：切换前预热「另一棵主页根」（治「网格切换掉帧」）───────────
        //
        // 「点击 → 首帧」埋点已经证明卡顿不在点击回调里，而在切换后的第一帧：
        // 另一棵主页根从 Collapsed 变 Visible 会作废布局，整棵子树重新 measure/arrange。
        // 这里在**最早的可预知时机**（鼠标移上去 / 按下去）就把这一帧的活提前付掉：
        // 调 PageTransition.PrewarmTarget（已公开的 API，内部只投递一个 Background 优先级的
        // DispatcherTimer，lead=0，立刻返回，绝不占用点击回调的同步预算）。
        // 它只动目标根的 Visibility（在同一次 Dispatcher 回调内还原）与布局，
        // 不碰 Opacity / RenderTransform / Width —— 不闪帧、不改任何外观。

        private void HomeModeButton_PrewarmOnHover(object sender, System.Windows.Input.MouseEventArgs e)
            => PrewarmOtherHomeRoot(sender);

        private void HomeModeButton_PrewarmOnPress(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => PrewarmOtherHomeRoot(sender);

        /// <summary>
        /// 按「这次碰的是哪颗按钮」推断要预热的主页根：
        /// 列表钮 -&gt; MinimalHomeRoot（极简主页），网格钮 -&gt; DashboardHomeRoot（信息主页）。
        /// 目标本来就在台前时 Prewarm 是幂等的（日志里是 already-visible），代价可忽略。
        /// </summary>
        private void PrewarmOtherHomeRoot(object? sender)
        {
            try
            {
                if (!PageTransition.ContentSwitchPrewarmEnabled) return;
                if (RootFrame?.Content is not Views.HomePage homePage) return;

                string name = ReferenceEquals(sender, HomeListModeButton) ? "MinimalHomeRoot" : "DashboardHomeRoot";
                if (homePage.FindName(name) is not FrameworkElement target) return;

                PageTransition.PrewarmTarget(target, 0.0);
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "PrewarmOtherHomeRoot");
            }
        }

        /// <summary>
        /// 「点击 → 首帧」埋点（home-mode-click 专用）。
        ///
        /// 为什么需要它：<c>HomeGridMode_Click</c> 里 <c>vm.IsMinimalHome = false</c> 的同步耗时
        /// （<c>[MotionPerf] home-mode-click sync=…</c>）只覆盖「点击回调本身」，
        /// 而用户感知到的「明显卡顿一下」发生在**切换后的第一帧**：
        /// 另一个主页根从 Collapsed 变 Visible 会作废布局、整棵子树重新 measure/arrange，
        /// 而且 250ms 的 scale/translate 动画期间那棵 1.28MP 的子树还拿不到 BitmapCache
        /// （超过 MotionAssist.MaxCacheDevicePixels = 1.2MP 上限），只能每帧重新栅格化。
        /// 所以这里挂一次性 <see cref="CompositionTarget.Rendering"/>：
        ///   * 第一次回调 = 渲染线程真的画出了切换后的第一帧 -> first-frame
        ///   * 之后 12 帧里最长的一帧 -> worst-frame（> 40ms 就是肉眼可见的掉帧）
        /// 常开、只挂 12 帧、不做任何可视树遍历，代价可以忽略。
        /// </summary>
        private void MeasureHomeModeFirstFrame(string detail)
        {
            try
            {
                double t0 = MotionPerf.NowMs;
                double last = t0;
                double worst = 0;
                int frames = 0;
                var timeline = new System.Text.StringBuilder();
                EventHandler? handler = null;

                handler = (s, e) =>
                {
                    double now = MotionPerf.NowMs;
                    double delta = now - last;
                    last = now;
                    frames++;

                    if (frames == 1)
                    {
                        Utilities.Logger.LogInfo(
                            $"[MotionPerf] home-mode-first-frame {now - t0:0.0}ms budget=100ms{detail}");
                    }
                    else
                    {
                        if (delta > worst) worst = delta;
                        if (timeline.Length > 0) timeline.Append(',');
                        timeline.Append(delta.ToString("0.0"));
                    }

                    if (frames < 12) return;

                    CompositionTarget.Rendering -= handler;
                    Utilities.Logger.LogInfo(
                        $"[MotionPerf] home-mode-frames 12 frames worst={worst:0.0}ms total={now - t0:0.0}ms " +
                        $"deltas=[{timeline}]{detail}");
                };

                CompositionTarget.Rendering += handler;
            }
            catch { }
        }

        /// <summary>✎ 铅笔按钮：切换「自定义 = 编辑小组件」，两态主页下都能切换。</summary>
        private void HomeEdit_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel vm)
                vm.IsHomeEditing = HomeEditButton.IsChecked == true;
        }

        #endregion
    }

    /// <summary>
    /// Maps a <see cref="Models.NewsItem"/> icon string (a Fluent symbol name such as
    /// "Sparkle24", or an emoji) onto a Fluent symbol; unknown values fall back to a
    /// neutral news glyph instead of failing the binding.
    /// </summary>
    public sealed class MainWindowNewsIconConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            string raw = value as string ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(raw) &&
                Enum.TryParse(raw.Trim(), true, out SymbolRegular symbol))
            {
                return symbol;
            }

            return SymbolRegular.News24;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
            => throw new NotSupportedException();
    }
}