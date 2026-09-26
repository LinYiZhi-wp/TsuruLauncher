using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using TsuruLauncher.Controls;
using TsuruLauncher.Models;
using TsuruLauncher.Services.Animation;
using TsuruLauncher.Utilities;
using TsuruLauncher.ViewModels;
using Path = System.IO.Path;

namespace TsuruLauncher.Views
{
    public partial class HomePage : Page
    {
        private readonly System.Diagnostics.Stopwatch _ctorClock = System.Diagnostics.Stopwatch.StartNew();


        public HomePage()
        {
            InitializeComponent();
            this.DataContext = ((App)System.Windows.Application.Current).MainWindow.DataContext;
            RefreshOverviewData();

            // 小组件卡片的显隐 / 进出动画 / 拖拽重排，统一由 HookWidgetCards 接管
            // （XAML 里那套 DataTrigger 硬切已经删掉）。
            HookWidgetCards();

            // TSURU_SELFTEST=widgetedit：让**应用自己**按顺序驱动一遍小组件编辑流程并打日志。
            // 存在意义：本机抢不到前台（Windows 前台锁定），外部脚本点不到按钮 ——
            // 那就把「点击」换成应用内部的命令调用，至少把逻辑链路验证掉。
            if (Environment.GetEnvironmentVariable("TSURU_SELFTEST") == "widgetedit")
                RunWidgetSelfTest();

            // 极简主页 <-> 信息主页：两态都在可视树里，切换时播「旧内容淡出 + 新内容过冲淡入」，
            // 不再靠 Visibility 硬切（Axolotl FloatingActionBar/App.vue 的 0.25s 过冲那组）。
            Loaded += HomePage_Loaded;
            Unloaded += HomePage_Unloaded;

            if (Environment.GetEnvironmentVariable("TSURU_SELFTEST") == "homemode")
                Dispatcher.BeginInvoke(new Action(RunHomeModeSelfTest),
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            if (Environment.GetEnvironmentVariable("TSURU_PERF") == "1")
                Utilities.Logger.LogInfo($"[Perf] HomePage 构造（XAML 解析）{_ctorClock.Elapsed.TotalMilliseconds:F1}ms");

            if (Environment.GetEnvironmentVariable("TSURU_PERF") == "1")
                Utilities.Logger.LogInfo($"[Perf] HomePage 构造（XAML 解析）{_ctorClock.Elapsed.TotalMilliseconds:F1}ms");
        }

        #region 两态切换（极简 / 信息主页）

        private bool? _appliedMinimalHome;

        /// <summary>
        /// 自检：TSURU_SELFTEST=homemode —— 反复切「简洁 ⇄ 网格」12 次。
        /// 如果切换会卡死，await 就永远回不来，日志会停在中间某一次。
        /// </summary>
        private async void RunHomeModeSelfTest()
        {
            try
            {
                if (DataContext is not MainViewModel vm) { Log("❌ 无 DataContext"); return; }

                Log("开始：简洁 ⇄ 网格 连续切换 12 次");
                for (int i = 0; i < 12; i++)
                {
                    vm.IsMinimalHome = (i % 2 == 0);

                    // ⚠ 关键：await 让 Dispatcher 跑完动画 + 布局。
                    //   如果重排进了死循环，Dispatcher 被占满，这一句永远回不来。
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    await System.Threading.Tasks.Task.Delay(350);
                    sw.Stop();

                    Log($"  {i + 1}/12 -> {(vm.IsMinimalHome ? "简洁" : "网格")}  " +
                        $"耗时 {sw.Elapsed.TotalMilliseconds:F0}ms  {MotionPerf.CacheSummary()}");
                }
                Log("✅ 12 次切换全部完成，没有卡死");
            }
            catch (Exception ex) { Log("❌ 异常：" + ex.Message); }
        }

        private static void Log(string msg)
            => Utilities.Logger.LogInfo("[HomeModeSelfTest] " + msg);

        private void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            if (Environment.GetEnvironmentVariable("TSURU_PERF") == "1")
                Utilities.Logger.LogInfo($"[Perf] HomePage Loaded 触发（距构造 {_ctorClock.Elapsed.TotalMilliseconds:F1}ms）");

            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged -= OnHomeViewModelPropertyChanged;
                vm.PropertyChanged += OnHomeViewModelPropertyChanged;
                ApplyHomeMode(vm.IsMinimalHome, animate: false);
            }
        }

        private void HomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.PropertyChanged -= OnHomeViewModelPropertyChanged;

            // 离开可视树时把两态动画就地落定：否则被换掉的页面上会留着
            // "半透明的 incoming + 还挂着 BitmapCache 的 outgoing"，下次回来接着乱。
            PageTransition.SettleElement(MinimalHomeRoot);
            PageTransition.SettleElement(DashboardHomeRoot);
            _homeModeSwitching = false;
        }

        private void OnHomeViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            if (e.PropertyName == nameof(MainViewModel.IsMinimalHome))
            {
                ApplyHomeMode(vm.IsMinimalHome, animate: true);
                return;
            }
        }

        /// <summary>两态动画正在启动（防重入；动画自身绝不允许再回调进 ApplyHomeMode）。</summary>
        private bool _homeModeSwitching;

        /// <summary>
        /// HomePage 此刻是不是"真的在屏幕上"的那一页。三条同时成立才算：
        ///   ① <see cref="FrameworkElement.IsLoaded"/>（在可视树里）；
        ///   ② <see cref="UIElement.IsVisible"/> 且挂着 <see cref="PresentationSource"/>（真的会被画出来）；
        ///   ③ 所属 <c>Frame</c> 的当前内容就是自己（不是"还活着但已经被换掉"的缓存页）。
        /// 不成立时一律**只切模式、不播动画**。
        /// 理由：对一个不可见子树做整页交叉淡入，会给新旧两个整页根各挂一张
        /// RenderAtScale=1.5 的 BitmapCache（1089x693 → 1634x1040 ≈ 6.8MB/张），
        /// 既没人看得到，又在连点 ▦/📋 时反复拉扯渲染线程的显存 —— 现场日志里的
        /// <c>UCEERR_RENDERTHREADFAILURE</c> 就是这么被点出来的。
        /// </summary>
        private bool IsLiveOnScreen()
        {
            try
            {
                if (!IsLoaded || !IsVisible) return false;
                if (PresentationSource.FromVisual(this) == null) return false;

                var nav = NavigationService;
                if (nav != null) return ReferenceEquals(nav.Content, this);

                // 没有 NavigationService（被别的宿主直接承载）：退化成"可视祖先里要有 Frame"
                DependencyObject? cur = this;
                for (int i = 0; i < 64 && cur != null; i++)
                {
                    if (cur is Frame) return true;
                    DependencyObject? parent = null;
                    try { parent = VisualTreeHelper.GetParent(cur); } catch { }
                    cur = parent;
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>
        /// 切到目标态。首次 / 无变化时直接落态；真正切换时走
        /// <see cref="PageTransition.PlayContentSwitch"/>：
        /// 旧内容 150ms ease 淡出后 Collapsed，新内容 250ms cubic-bezier(0.15, 1.4, 0.64, 0.96)
        /// 从 scale(0.96) + translateY(8px) + opacity 0 过冲浮入；切换期间两态短暂共存，
        /// 各自挂 BitmapCache，结束后只把不可见的那一个落成 Collapsed。
        /// </summary>
        private void ApplyHomeMode(bool minimal, bool animate)
        {
            if (MinimalHomeRoot == null || DashboardHomeRoot == null) return;

            bool changed = _appliedMinimalHome != minimal;
            _appliedMinimalHome = minimal;

            var incoming = minimal ? (FrameworkElement)MinimalHomeRoot : DashboardHomeRoot;
            var outgoing = minimal ? (FrameworkElement)DashboardHomeRoot : MinimalHomeRoot;

            // ── 防御：以下任一情况都只落状态、不播动画 ─────────────────────────────
            //   * HomePage 不在可视树 / 不是当前页 / 被 Collapsed（缓存页、正在被换掉）
            //   * 目标态没变
            //   * 调用方要求不播
            //   * 上一次切换还在启动过程中（防重入；动画回调不会再进来改 IsMinimalHome）
            bool live = IsLiveOnScreen();
            if (!live || !animate || !changed || _homeModeSwitching)
            {
                string reason = !live ? "home-page-not-on-screen"
                    : !changed ? "no-change"
                    : !animate ? "animate-false"
                    : "reentrant";

                // 就地落定两个态：清时钟 + Opacity=1 + 清位移 + 摘缓存
                PageTransition.SettleElement(outgoing);
                PageTransition.SettleElement(incoming);
                outgoing.Visibility = Visibility.Collapsed;
                incoming.Visibility = Visibility.Visible;
                incoming.Opacity = 1;

                MotionPerf.NoteSkip("home-mode", reason);
                return;
            }

            _homeModeSwitching = true;
            try
            {
                using (MotionPerf.Measure("home-mode", minimal ? "-> minimal" : "-> dashboard"))
                {
                    PageTransition.PlayContentSwitch(outgoing, incoming, forward: !minimal);
                }

                Logger.LogInfo(
                    $"[MotionPerf] home mode -> {(minimal ? "minimal" : "dashboard")} " +
                    $"leave={AxolotlMotion.ContentSwitchLeaveMs}ms ease " +
                    $"enter=scale {AxolotlMotion.ContentSwitchFromScale}->1 + translateY {AxolotlMotion.ContentSwitchOffsetPx}px->0 + opacity, " +
                    $"{AxolotlMotion.ContentSwitchMs}ms cubic-bezier(0.15,1.4,0.64,0.96) " +
                    $"(was scale 0.96->1 / translateY 8px->0) " +
                    $"overlapped=true live={live} animations={PageTransition.AnimationsEnabled} " +
                    MotionPerf.CacheSummary());
            }
            finally
            {
                _homeModeSwitching = false;
            }
        }

        #endregion

        /// <summary>Refreshes every dashboard surface (stats, player, versions, news).</summary>
        public void RefreshOverviewData()
        {
            if (DataContext is not MainViewModel vm) return;

            vm.RefreshStats();
            vm.RefreshLibraryStats();
            vm.RefreshPlayerCard();
            vm.RefreshNews();
            vm.RefreshJavaVersion();
            vm.RefreshVersionTexts(vm.SelectedVersion);
            vm.RefreshLibraryStats();

            vm.InitializeHomeWidgets();

            // 版本列表（极简主页实例卡 / 置顶实例小件）直接绑定 GameVersions / SelectedVersion
            if (vm.IsMinimalHome)
            {
                Logger.LogInfo($"[Audit] minimal-home versions={vm.GameVersions.Count} " +
                               $"hasVersions={vm.HasVersions} selected={vm.SelectedVersion?.Id ?? "<none>"}");
            }
        }

        #region Version actions

        /// <summary>极简主页的「创建实例」：先到下载页装一个版本。</summary>
        // ─────────── 小组件编辑（把 VM 里早就写好、但一直没接上的那套命令接到 UI）───────────

        /// <summary>「＋ 添加小组件」面板里的某一项被点：Tag 上挂的是 HomeWidget（只用它的 Kind）。</summary>
        private void AddWidget_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Models.HomeWidget widget &&
                DataContext is ViewModels.MainViewModel vm)
            {
                if (vm.HasWidget(widget.Kind))
                {
                    vm.NotificationService.ShowSuccess("已存在", $"{widget.Title} 已经在首页上了。");
                    return;
                }
                vm.AddWidgetCommand.Execute(widget.Kind);
                vm.NotificationService.ShowSuccess("已添加", $"{widget.Title} 已加到首页。");
            }
        }

        /// <summary>「紧凑模式」：行高 160 → 122，一屏能放下更多小件（VM 里有 ToggleCompactCommand）。</summary>
        private void ToggleCompact_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel vm) vm.ToggleCompactCommand.Execute(null);
        }

        /// <summary>「完成」：退出编辑态（VM 侧会顺手关掉添加面板）。</summary>
        private void FinishHomeEdit_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel vm) vm.IsHomeEditing = false;
        }

        // ═══════════════ 小组件网格（数据驱动）═══════════════
        //
        // 仪表盘改成 ItemsControl 绑 HomeWidgets 之后，卡片不再是 x:Name 字段，
        // 增删动画交给「项容器」的 Loaded 事件；拖拽重排用 ItemsControl 的命中测试。

        /// <summary>
        /// 「✕ 移除」：**先播退出动画，播完再真的从集合里删**。
        ///
        /// 为什么不能直接调 RemoveWidgetCommand —— ItemsControl 在集合移除时会**立刻销毁项容器**，
        /// 容器都没了就没地方播动画。所以这里先对 ContentPresenter 播 180ms 淡出 + 缩到 0.94，
        /// 在 onCompleted 里才调命令。（用户反复要求「删除组件也要过渡动画」。）
        /// </summary>
        private void RemoveWidget_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not Models.HomeWidget widget) return;
            RemoveWidgetAnimated(widget);
        }

        // ─────────── Axolotl 底部工具条 ───────────

        private void WidgetToolbarAdd_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel vm)
                vm.IsWidgetPickerOpen = !vm.IsWidgetPickerOpen;
        }

        private void WidgetToolbarDone_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel vm) vm.IsHomeEditing = false;
        }

        private void WidgetToolbarGrid_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel vm) vm.IsFreeLayout = false;
            QueueWidgetPacking();
        }

        private void WidgetToolbarFree_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.MainViewModel vm)
            {
                // 切到 free 之前先把当前装箱结果写成每个小组件的初始坐标（Axolotl enableFreeHomeDashboard 同做法）
                if (!vm.IsFreeLayout) CapturePackedPositionsAsFree();
                vm.IsFreeLayout = true;
            }
            QueueWidgetPacking();
        }

        /// <summary>把当前 grid 装箱结果固化成 free 模式的初始坐标。</summary>
        private void CapturePackedPositionsAsFree()
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;
            int cols = Models.HomeWidget.ColumnCount;
            var occupied = new List<bool[]>();
            bool Fits(int c, int r, int w, int h)
            {
                if (c + w > cols) return false;
                for (int y = r; y < r + h; y++)
                    for (int x = c; x < c + w; x++)
                        if (y < occupied.Count && occupied[y][x]) return false;
                return true;
            }
            foreach (var w in vm.HomeWidgets)
            {
                int sc = w.SpanColumns, sr = w.SpanRows;
                int row = 0, col = 0;
                while (!Fits(col, row, sc, sr)) { col++; if (col >= cols) { col = 0; row++; } }
                for (int y = row; y < row + sr; y++)
                {
                    while (occupied.Count <= y) occupied.Add(new bool[cols]);
                    for (int x = col; x < col + sc; x++) occupied[y][x] = true;
                }
                w.X = col; w.Y = row;
            }
        }

        /// <summary>
        /// Axolotl 的「小组件选项」菜单：点卡片右上角的 ⣿ 手柄弹出。
        /// 菜单项完全按 Axolotl 来（HomeDashboard.vue）：
        ///   尺寸 1x1 / 2x1 / 1x2 / 2x2（**只列该种类允许的档**，当前档打勾）
        ///   ── 向前移动 / 向后移动（首/末项自动置灰）
        ///   ── 删除小组件（红色）
        /// </summary>
        private void WidgetHandle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not Models.HomeWidget widget) return;
            if (DataContext is not ViewModels.MainViewModel vm) return;

            var menu = new ContextMenu
            {
                PlacementTarget = fe,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                Background = TryBrush("Surface3Brush"),
                BorderBrush = TryBrush("GlassBorderBrush"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4)
            };

            // ① 尺寸
            foreach (var size in ViewModels.MainViewModel.SizeOptionsFor(widget.Kind))
            {
                string sz = size;
                var mi = new MenuItem
                {
                    Header = "尺寸 " + sz,
                    IsCheckable = true,
                    IsChecked = string.Equals(widget.SizeKey, sz, StringComparison.Ordinal)
                };
                mi.Click += (a, b) => vm.SetWidgetSizeTo(widget, sz);
                menu.Items.Add(mi);
            }

            menu.Items.Add(new Separator());

            // ② 向前 / 向后移动
            int index = vm.HomeWidgets.IndexOf(widget);
            var earlier = new MenuItem { Header = "向前移动", IsEnabled = index > 0 };
            earlier.Click += (a, b) => vm.MoveWidget(widget, -1);
            menu.Items.Add(earlier);

            var later = new MenuItem { Header = "向后移动", IsEnabled = index >= 0 && index < vm.HomeWidgets.Count - 1 };
            later.Click += (a, b) => vm.MoveWidget(widget, 1);
            menu.Items.Add(later);

            menu.Items.Add(new Separator());

            // ③ 删除
            var del = new MenuItem { Header = "删除小组件", Foreground = TryBrush("DangerBrush") };
            del.Click += (a, b) => RemoveWidgetAnimated(widget);
            menu.Items.Add(del);

            menu.IsOpen = true;
        }

        private static System.Windows.Media.Brush? TryBrush(string key)
        {
            try { return System.Windows.Application.Current?.TryFindResource(key) as System.Windows.Media.Brush; }
            catch { return null; }
        }

        /// <summary>带退出动画的移除（✕ 按钮与自检共用同一条路径）。</summary>
        private void RemoveWidgetAnimated(Models.HomeWidget widget)
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;
            if (widget == null || vm.HomeWidgets.Count <= 1) return;

            // 找到这张卡对应的项容器
            FrameworkElement? item = null;
            try
            {
                item = WidgetHost?.ItemContainerGenerator.ContainerFromItem(widget) as FrameworkElement;
            }
            catch { }

            if (item == null)
            {
                vm.RemoveWidgetCommand.Execute(widget);   // 拿不到容器就退回直接删
                return;
            }

            PageTransition.PlayPopup(item, show: false, fromScale: 0.94,
                durationMs: 180, ease: AxolotlMotion.EaseIn, useCache: false,
                onCompleted: () => vm.RemoveWidgetCommand.Execute(widget));
        }

        /// <summary>项容器生成后播一次弹入（=「添加组件」的过渡动画）。</summary>
        private int _lastPackedWidgetCount = -1;

        private void WidgetItem_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe) return;

            // ⚠⚠ 这里**不能**无条件 QueueWidgetPacking。
            //   容器加载 → 重排 → 布局失效 → 容器重新加载 → 再重排 → … 会形成死循环，
            //   表现就是「简洁模式 ⇄ 网格模式」切换直接卡死。
            //   只有**组件数量变了**（新增/删除）才需要重排；
            //   尺寸变化由 WidgetSize_Changed 那边负责。
            int count = (DataContext as ViewModels.MainViewModel)?.HomeWidgets.Count ?? -1;
            if (count != _lastPackedWidgetCount)
            {
                _lastPackedWidgetCount = count;
                QueueWidgetPacking();
            }
            if (fe.Tag as string == "anim-done") return;
            fe.Tag = "anim-done";
            Utilities.Logger.LogInfo("[WidgetGrid] item kind=" +
                ((fe.DataContext as Models.HomeWidget)?.Kind ?? "?") +
                " size=" + (fe.DataContext as Models.HomeWidget)?.SizeKey +
                " rendered=" + (fe.ActualWidth > 1 && fe.ActualHeight > 1));
            fe.Opacity = 0;
            PageTransition.PlayPopup(fe, show: true, fromScale: 0.94,
                durationMs: 260, ease: AxolotlMotion.OvershootEase, useCache: false);
        }

        private void HookWidgetCards()
        {
            if (WidgetHost != null)
            {
                // 容器宽度变化 → 重算每张卡的像素宽（1x1 = 一列、2x1 = 两列）
                WidgetHost.SizeChanged -= WidgetHost_SizeChanged;
                WidgetHost.SizeChanged += WidgetHost_SizeChanged;
                ApplyWidgetContainerWidth();
            }

            // 编辑态下可拖拽重排
            if (WidgetHost != null)
            {
                WidgetHost.PreviewMouseLeftButtonDown -= WidgetHost_MouseDown;
                WidgetHost.PreviewMouseMove -= WidgetHost_MouseMove;
                WidgetHost.PreviewMouseLeftButtonUp -= WidgetHost_MouseUp;
                WidgetHost.PreviewMouseLeftButtonDown += WidgetHost_MouseDown;
                WidgetHost.PreviewMouseMove += WidgetHost_MouseMove;
                WidgetHost.PreviewMouseLeftButtonUp += WidgetHost_MouseUp;
            }
        }

        private void WidgetHost_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyWidgetContainerWidth();

        // ═══════════ Axolotl packHomeWidgets 的 C# 移植 ═══════════
        // 首次适应（first-fit）装箱：按顺序给每个小组件找**第一个装得下的位置**。
        // 这是 Axolotl 的 grid 布局模式（home-dashboard.ts: packHomeWidgets），
        // 和 WrapPanel 的区别就是**后面的卡会回填前面的空洞**。
        // ── 重排合并 ────────────────────────────────────────────────────
        // ⚠ 为什么要合并：`ApplyWidgetPacking` 会写 Grid.Row/Column/RowSpan/ColumnSpan，
        //   每次写都让布局失效 → 触发一次完整 layout pass。
        //   而它在 7 个地方被调（小组件增删、尺寸变化、模式切换、容器尺寸变化…），
        //   启动时 5 个小组件各渲染一次就是 **5 次完整重排 + 5 次布局失效**。
        //   实测启动日志里 `[WidgetPack]` 连打 5 遍。
        //
        //   改成「标脏 + 下一帧统一做一次」：同一帧内调 N 次只真正排 1 次。
        private bool _packQueued;

        // ── 频率熔断（防死循环卡死）────────────────────────────────────
        // ⚠⚠ 背景：`WidgetItem_Loaded`（每个小组件加载完）会调 QueueWidgetPacking。
        //   如果重排本身又导致容器重新加载，就形成
        //      加载 → 重排 → 布局失效 → 重新加载 → 重排 → …
        //   的循环。而每轮还会写一次日志（**同步文件 IO**），
        //   结果就是界面彻底卡死（用户实测：简洁模式 ⇄ 网格模式切换卡死）。
        //
        //   这里加个频率熔断：1 秒内重排超过 N 次就认定进了循环、直接停手。
        //   **宁可布局差一点，也绝不能卡死。**
        private string? _lastPackSignature;
        private int _packStreak;
        private DateTime _packStreakStartUtc = DateTime.MinValue;
        private const int PackStreakLimit = 30;

        private void QueueWidgetPacking()
        {
            if (_packQueued) return;

            var now = DateTime.UtcNow;
            if ((now - _packStreakStartUtc).TotalSeconds > 1.0)
            {
                _packStreakStartUtc = now;
                _packStreak = 0;
            }
            if (++_packStreak > PackStreakLimit)
            {
                if (_packStreak == PackStreakLimit + 1)
                    Utilities.Logger.LogInfo(
                        $"[WidgetPack] ⚠ 熔断：1 秒内重排超过 {PackStreakLimit} 次，已暂停（防止卡死）");
                return;
            }

            _packQueued = true;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                // ⚠ 这里必须调 ApplyWidgetPacking（真正干活的那个），不是 QueueWidgetPacking。
                //   而且 _packQueued 要**等排完再复位** —— 否则排的过程中再被触发会无限排队。
                try { ApplyWidgetPacking(); }
                finally { _packQueued = false; }
            }), System.Windows.Threading.DispatcherPriority.Render);
        }

        private void ApplyWidgetPacking()
        {
            if (WidgetHost == null) return;
            var panel = FindItemsPanel();
            if (panel == null) return;

            int cols = Models.HomeWidget.ColumnCount;
            var widgets = (DataContext as ViewModels.MainViewModel)?.HomeWidgets;
            if (widgets == null) return;

            // 列定义：cols 等宽
            if (panel.ColumnDefinitions.Count != cols)
            {
                panel.ColumnDefinitions.Clear();
                for (int i = 0; i < cols; i++)
                    panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }

            // free 模式：直接用每个小组件自己的 (X, Y)，不做装箱
            if ((DataContext as ViewModels.MainViewModel)?.IsFreeLayout == true)
            {
                int maxRowFree = 1;
                foreach (var w in widgets)
                {
                    var c0 = WidgetHost.ItemContainerGenerator.ContainerFromItem(w) as FrameworkElement;
                    if (c0 == null) continue;
                    Grid.SetColumn(c0, Math.Min(w.X, Math.Max(0, cols - w.SpanColumns)));
                    Grid.SetRow(c0, Math.Max(0, w.Y));
                    Grid.SetColumnSpan(c0, w.SpanColumns);
                    Grid.SetRowSpan(c0, w.SpanRows);
                    maxRowFree = Math.Max(maxRowFree, w.Y + w.SpanRows);
                }
                if (panel.RowDefinitions.Count != maxRowFree)
                {
                    panel.RowDefinitions.Clear();
                    for (int i = 0; i < maxRowFree; i++)
                        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = Models.HomeWidget.GridRowHeight });
                }
                return;
            }

            // 装箱
            var occupied = new List<bool[]>();
            bool Fits(int c, int r, int w, int h)
            {
                if (c + w > cols) return false;
                for (int y = r; y < r + h; y++)
                    for (int x = c; x < c + w; x++)
                        if (y < occupied.Count && occupied[y][x]) return false;
                return true;
            }

            var placed = new List<(Models.HomeWidget W, int Col, int Row, int ColSpan, int RowSpan)>();
            foreach (var w in widgets)
            {
                int sc = w.SpanColumns, sr = w.SpanRows;
                int row = 0, col = 0;
                while (!Fits(col, row, sc, sr))
                {
                    col++;
                    if (col >= cols) { col = 0; row++; }
                }
                for (int y = row; y < row + sr; y++)
                {
                    while (occupied.Count <= y) occupied.Add(new bool[cols]);
                    for (int x = col; x < col + sc; x++) occupied[y][x] = true;
                }
                placed.Add((w, col, row, sc, sr));
            }

            // 行定义：每行固定 GridRowHeight（多行跨度靠 RowSpan 自然叠加）
            int maxRow = placed.Count == 0 ? 1 : placed.Max(p => p.Row + p.RowSpan);
            if (panel.RowDefinitions.Count != maxRow)
            {
                panel.RowDefinitions.Clear();
                for (int i = 0; i < maxRow; i++)
                    panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = Models.HomeWidget.GridRowHeight });
            }

            // ⚠ 只在**布局真的变了**时才写日志 —— 每次重排都 LogInfo 是同步文件 IO，
            //   一旦进了循环就是雪上加霜（实测会直接把界面拖死）。
            string signature = cols + "x" + maxRow + "|" + string.Join(" ", placed.Select(p =>
                p.W.Kind + "@" + p.Col + "," + p.Row + "(" + p.ColSpan + "x" + p.RowSpan + ")"));
            if (signature != _lastPackSignature)
            {
                _lastPackSignature = signature;
                Utilities.Logger.LogInfo("[WidgetPack] " + signature);
            }

            // 写到每项的容器上
            foreach (var p in placed)
            {
                var container = WidgetHost.ItemContainerGenerator.ContainerFromItem(p.W) as FrameworkElement;
                if (container == null) continue;
                Grid.SetColumn(container, p.Col);
                Grid.SetRow(container, p.Row);
                Grid.SetColumnSpan(container, p.ColSpan);
                Grid.SetRowSpan(container, p.RowSpan);
            }
        }

        /// <summary>拿到 ItemsControl 里那个 Grid 面板。</summary>
        private System.Windows.Controls.Grid? FindItemsPanel()
        {
            return FindGrid(WidgetHost);
        }

        private static System.Windows.Controls.Grid? FindGrid(DependencyObject? d)
        {
            if (d == null) return null;
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(d, i);
                if (c is System.Windows.Controls.Grid g) return g;
                var r = FindGrid(c);
                if (r != null) return r;
            }
            return null;
        }

        /// <summary>小组件编辑流程自检（TSURU_SELFTEST=widgetedit）。</summary>
        private void RunWidgetSelfTest()
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;
            var t = new System.Windows.Threading.DispatcherTimer(
                System.TimeSpan.FromMilliseconds(1000),
                System.Windows.Threading.DispatcherPriority.Background, (a, b) => { }, Dispatcher);
            int step = 0;
            t.Tick += (s2, e2) =>
            {
                step++;
                try
                {
                    switch (step)
                    {
                        case 1:
                            vm.IsHomeEditing = true;
                            Utilities.Logger.LogInfo("[SelfTest] 1 编辑态开 editing=" + vm.IsHomeEditing +
                                " pill=" + FloatingPillVisibilityProbe());
                            break;
                        case 2:
                            vm.ToggleWidgetPickerCommand.Execute(null);
                            Utilities.Logger.LogInfo("[SelfTest] 2 打开添加面板 picker=" + vm.IsWidgetPickerOpen);
                            break;
                        case 3:
                            vm.IsHomeEditing = false;
                            Utilities.Logger.LogInfo("[SelfTest] 3 退出编辑态 editing=" + vm.IsHomeEditing +
                                " picker=" + vm.IsWidgetPickerOpen + "  <= 期望 picker=False");
                            break;
                        case 4:
                            vm.AddWidgetCommand.Execute("java");
                            Utilities.Logger.LogInfo("[SelfTest] 4 添加 java -> 共 " + vm.HomeWidgets.Count +
                                " 个: " + string.Join(",", vm.HomeWidgets.Select(w => w.Kind)));
                            break;
                        case 5:
                            vm.SwapWidgetOrder("stats", "worlds");
                            Utilities.Logger.LogInfo("[SelfTest] 5 拖拽交换 stats<->worlds -> " +
                                string.Join(",", vm.HomeWidgets.Select(w => w.Kind)));
                            break;
                        case 6:
                            vm.CycleWidgetSizeCommand.Execute(vm.HomeWidgets.FirstOrDefault(w => w.Kind == "news"));
                            Utilities.Logger.LogInfo("[SelfTest] 6 改尺寸 news -> " +
                                (vm.HomeWidgets.FirstOrDefault(w => w.Kind == "news")?.SizeKey ?? "?"));
                            break;
                        case 7:
                            var toRemove = vm.HomeWidgets.FirstOrDefault(w => w.Kind == "java");
                            RemoveWidgetAnimated(toRemove);
                            Utilities.Logger.LogInfo("[SelfTest] 7 移除 java（走带退出动画的路径）-> 共 " + vm.HomeWidgets.Count + " 个");
                            break;
                        case 8:
                            // 上一拍的移除是**延迟**的（180ms 动画播完才删），所以到这里才检查结果
                            Utilities.Logger.LogInfo("[SelfTest] 8 移除动画播完后 -> 共 " + vm.HomeWidgets.Count +
                                " 个: " + string.Join(",", vm.HomeWidgets.Select(w => w.Kind)) +
                                "  <= 期望：不含 java，数量比上一拍少 1");
                            break;
                        case 9:
                            // Axolotl 选项菜单里的「尺寸 2x2」——指定档位而不是循环切换
                            var news = vm.HomeWidgets.FirstOrDefault(w => w.Kind == "news");
                            vm.SetWidgetSizeTo(news, "2x2");
                            Utilities.Logger.LogInfo("[SelfTest] 9 菜单改尺寸 news -> " +
                                (news?.SizeKey ?? "?") + "  <= 期望 2x2");
                            break;
                        case 10:
                            var acc = vm.HomeWidgets.FirstOrDefault(w => w.Kind == "account");
                            int before = vm.HomeWidgets.IndexOf(acc);
                            vm.MoveWidget(acc, -1);
                            Utilities.Logger.LogInfo("[SelfTest] 10 向前移动 account " + before + " -> " +
                                vm.HomeWidgets.IndexOf(acc) + "  顺序: " +
                                string.Join(",", vm.HomeWidgets.Select(w => w.Kind)));
                            break;
                        case 11:
                            var acc2 = vm.HomeWidgets.FirstOrDefault(w => w.Kind == "account");
                            int b2 = vm.HomeWidgets.IndexOf(acc2);
                            vm.MoveWidget(acc2, 1);
                            Utilities.Logger.LogInfo("[SelfTest] 11 向后移动 account " + b2 + " -> " +
                                vm.HomeWidgets.IndexOf(acc2));
                            break;
                        case 12:
                            CapturePackedPositionsAsFree();
                            vm.IsFreeLayout = true;
                            QueueWidgetPacking();
                            Utilities.Logger.LogInfo("[SelfTest] 12 切自由摆放 isFree=" + vm.IsFreeLayout +
                                " 坐标: " + string.Join(" ", vm.HomeWidgets.Select(w => w.Kind + "@" + w.X + "," + w.Y)));
                            break;
                        case 13:
                            vm.PlaceWidgetAt(vm.HomeWidgets.FirstOrDefault(w => w.Kind == "account"), 2, 2);
                            Utilities.Logger.LogInfo("[SelfTest] 13 自由摆放 account -> " +
                                (vm.HomeWidgets.FirstOrDefault(w => w.Kind == "account") is { } a2 ? a2.X + "," + a2.Y : "?"));
                            break;
                        case 14:
                            vm.UndoLayoutCommand.Execute(null);
                            Utilities.Logger.LogInfo("[SelfTest] 14 撤销 -> " +
                                (vm.HomeWidgets.FirstOrDefault(w => w.Kind == "account") is { } a3 ? a3.X + "," + a3.Y : "?") +
                                "  <= 期望回到 2,0 附近");
                            break;
                        case 15:
                            vm.IsFreeLayout = false;
                            QueueWidgetPacking();
                            Utilities.Logger.LogInfo("[SelfTest] 15 切回网格 isFree=" + vm.IsFreeLayout);
                            t.Stop();
                            Utilities.Logger.LogInfo("[SelfTest] DONE");
                            break;
                    }
                }
                catch (Exception ex) { Utilities.Logger.LogError(ex, "WidgetSelfTest step=" + step); }
            };
            t.Start();
        }

        private string FloatingPillVisibilityProbe()
        {
            try
            {
                var w = (App)System.Windows.Application.Current;
                var pill = w.MainWindow?.FindName("FloatingPill") as FrameworkElement;
                return pill?.Visibility.ToString() ?? "?";
            }
            catch { return "?"; }
        }

        /// <summary>把仪表盘实际可用宽度写进 HomeWidget，并让所有卡重算 PixelWidth。</summary>
        private void ApplyWidgetContainerWidth()
        {
            if (WidgetHost == null) return;
            double w = WidgetHost.ActualWidth;
            if (w < 100) return;
            if (Math.Abs(Models.HomeWidget.ContainerWidth - w) < 0.5) return;
            Models.HomeWidget.ContainerWidth = w;
            if (DataContext is ViewModels.MainViewModel vm)
                foreach (var widget in vm.HomeWidgets) widget.RefreshSize();
            QueueWidgetPacking();
        }

        private FrameworkElement? _dragItem;
        private Point _dragOrigin;
        private bool _dragMoved;
        private bool _dragFromHandle;

        /// <summary>从命中元素往上找「⣿ 手柄」（Tag 是 HomeWidget 的 WidgetDragHandle 按钮）。</summary>
        private static Button? HandleUnder(DependencyObject? d)
        {
            while (d != null)
            {
                if (d is Button b && b.Tag is Models.HomeWidget &&
                    b.Style == System.Windows.Application.Current?.TryFindResource("WidgetDragHandle"))
                    return b;
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        private static Button? FindHandle(DependencyObject root)
        {
            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (c is Button b && b.Tag is Models.HomeWidget &&
                    b.Style == System.Windows.Application.Current?.TryFindResource("WidgetDragHandle"))
                    return b;
                var r = FindHandle(c);
                if (r != null) return r;
            }
            return null;
        }

        private FrameworkElement? ItemUnder(DependencyObject? d)
        {
            while (d != null)
            {
                if (d is ContentPresenter cp && cp.DataContext is Models.HomeWidget) return cp;
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            }
            return null;
        }

        private void WidgetHost_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (DataContext is not ViewModels.MainViewModel vm || !vm.IsHomeEditing) return;

            // ⣿ 手柄：**按下去也能拖**（Axolotl 就是拖手柄）。其它按钮（比如卡内自己的操作按钮）
            // 仍然排除在外，免得抢掉它们的点击。
            var src = e.OriginalSource as DependencyObject;
            _dragFromHandle = src != null && HandleUnder(src) != null;
            if (!_dragFromHandle && src != null && HitButton(src)) return;

            var item = ItemUnder(src);
            if (item == null) return;
            _dragItem = item;
            _dragOrigin = e.GetPosition(this);
            _dragMoved = false;
            item.CaptureMouse();
        }

        private void WidgetHost_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_dragItem == null || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed) return;
            var p = e.GetPosition(this);
            if (!_dragMoved)
            {
                if (Math.Abs(p.X - _dragOrigin.X) < 6 && Math.Abs(p.Y - _dragOrigin.Y) < 6) return;
                _dragMoved = true;
                _dragItem.Opacity = 0.7;
                Panel.SetZIndex(_dragItem, 999);
            }
            var t = _dragItem.RenderTransform as TranslateTransform;
            if (t == null) { t = new TranslateTransform(); _dragItem.RenderTransform = t; }
            t.X = p.X - _dragOrigin.X;
            t.Y = p.Y - _dragOrigin.Y;
        }

        private void WidgetHost_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_dragItem == null) return;
            var item = _dragItem;
            bool fromHandle = _dragFromHandle;
            _dragItem = null;
            _dragFromHandle = false;
            try { item.ReleaseMouseCapture(); } catch { }

            if (!_dragMoved)
            {
                // 手柄上按下但没拖动 = 点了一下手柄 → 弹「小组件选项」菜单
                if (fromHandle && item.DataContext is Models.HomeWidget w0)
                {
                    var handle = FindHandle(item);
                    if (handle != null) WidgetHandle_Click(handle, new RoutedEventArgs());
                }
                return;
            }
            _dragMoved = false;
            if (item.RenderTransform is TranslateTransform tt) { tt.X = 0; tt.Y = 0; }
            item.Opacity = 1;
            Panel.SetZIndex(item, 0);

            // free 模式：按落点算格子坐标，直接放过去（Axolotl free 拖拽）
            if (DataContext is ViewModels.MainViewModel fvm && fvm.IsFreeLayout &&
                item.DataContext is Models.HomeWidget fw)
            {
                var panel = FindItemsPanel();
                if (panel != null)
                {
                    var pt = e.GetPosition(panel);
                    double pitchX = Models.HomeWidget.ColumnWidth + Models.HomeWidget.Gap;
                    double pitchY = fw.RowHeight + Models.HomeWidget.Gap;
                    int col = (int)Math.Round(pt.X / pitchX);
                    int row = (int)Math.Round(pt.Y / pitchY);
                    fvm.PlaceWidgetAt(fw, col, row);
                    QueueWidgetPacking();
                }
                return;
            }

            var hit = System.Windows.Media.VisualTreeHelper.HitTest(this, e.GetPosition(this))?.VisualHit;
            var target = ItemUnder(hit);
            if (target == null || ReferenceEquals(target, item)) return;
            if (item.DataContext is not Models.HomeWidget wa) return;
            if (target.DataContext is not Models.HomeWidget wb) return;

            if (DataContext is ViewModels.MainViewModel vm)
            {
                vm.SwapWidgetOrder(wa.Kind, wb.Kind);
                foreach (var c in new[] { item, target })
                {
                    PageTransition.PlayPopup(c, show: true, fromScale: 0.97, durationMs: 220,
                        ease: AxolotlMotion.OvershootEase, useCache: false);
                }
            }
        }

        private static bool HitButton(DependencyObject d)
        {
            while (d != null)
            {
                if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        private void CreateInstance_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.Application.Current.MainWindow is MainWindow main)
                main.NavigateTo("download");
        }

        private void OpenVersionSelector_Click(object sender, RoutedEventArgs e)
        {
            NavigationService?.Navigate(new VersionSelectorPage(async (selectedGv) =>
            {
                if (DataContext is not MainViewModel vm) return;

                var match = vm.GameVersions.FirstOrDefault(v => v.Id == selectedGv.Id);
                if (match == null)
                {
                    await vm.LoadVersionsAsync();
                    match = vm.GameVersions.FirstOrDefault(v => v.Id == selectedGv.Id);
                }

                if (match != null)
                {
                    vm.SelectedVersion = match;
                    RefreshOverviewData();
                }
            }));
        }

        private void OpenVersionSettings_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm || vm.SelectedVersion == null)
            {
                iOS26Dialog.Show(GetString("Msg_SelectVersionFirst"), GetString("Title_Warning"), DialogIcon.Info);
                return;
            }
            NavigationService?.Navigate(new VersionSettingsPage(vm.SelectedVersion));
        }

        private void OpenModManager_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm || vm.SelectedVersion == null)
            {
                iOS26Dialog.Show(GetString("Msg_SelectVersionFirst"), GetString("Title_Warning"), DialogIcon.Info);
                return;
            }
            NavigationService?.Navigate(new VersionSettingsPage(vm.SelectedVersion, "mods"));
        }

        #endregion

        #region Quick actions

        private void OpenGameDir_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm) return;

            string gamePath = vm.ConfigService.Settings.GamePath;
            if (!string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath))
            {
                Process.Start("explorer.exe", $"\"{gamePath}\"");
            }
            else
            {
                iOS26Dialog.Show(GetString("Msg_GameDirNotFound"), GetString("Title_Warning"), DialogIcon.Warning);
            }
        }

        private void OpenLogFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string logPath = Logger.GetLogPath();
                if (File.Exists(logPath))
                {
                    Process.Start("explorer.exe", $"/select,\"{logPath}\"");
                }
                else
                {
                    string? dir = Path.GetDirectoryName(logPath);
                    if (dir != null && Directory.Exists(dir))
                        Process.Start("explorer.exe", $"\"{dir}\"");
                    else
                        iOS26Dialog.Show(GetString("Msg_LogNotGenerated"), GetString("Title_Warning"), DialogIcon.Info);
                }
            }
            catch (Exception ex)
            {
                iOS26Dialog.Show($"无法打开日志: {ex.Message}", "错误", DialogIcon.Error);
            }
        }

        /// <summary>点「最近世界」卡片：在资源管理器里打开该存档文件夹。</summary>
        private void World_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Models.WorldEntry world && !string.IsNullOrEmpty(world.Path))
            {
                try { Process.Start("explorer.exe", $"\"{world.Path}\""); }
                catch (Exception ex) { iOS26Dialog.Show($"无法打开存档: {ex.Message}", "错误", DialogIcon.Error); }
            }
        }

        private void NewsItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is NewsItem item && !string.IsNullOrEmpty(item.Url))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(item.Url) { UseShellExecute = true });
                }
                catch { }
            }
        }

        #endregion

        private string GetString(string key)
        {
            try
            {
                if (Application.Current.FindResource(key) is string s) return s;
            }
            catch { }
            return key;
        }
    }

    /// <summary>按 <see cref="Models.HomeWidget.Kind"/> 选卡片模板。</summary>
    public sealed class WidgetTemplateSelector : System.Windows.Controls.DataTemplateSelector
    {
        public override System.Windows.DataTemplate? SelectTemplate(object item, System.Windows.DependencyObject container)
        {
            if (item is not Models.HomeWidget w) return null;
            string key = w.Kind switch
            {
                "instance" => "TplInstance",
                "stats" => "TplStats",
                "worlds" => "TplWorlds",
                "account" => "TplAccount",
                "news" => "TplNews",
                "java" => "TplJava",
                "shortcuts" => "TplShortcuts",
                "greeting" => "TplGreeting",
                _ => "TplStats"
            };
            // ⚠ 必须用传进来的 container 沿**可视树**往上找（TryFindResource），
            //   不能用 Application.Current / MainWindow —— 这些模板定义在 HomePage.Resources 里，
            //   那两条查找链根本够不到，结果 SelectTemplate 返回 null，
            //   WPF 就退回显示对象的 ToString()（页面上直接出现 "TsuruLauncher.Models.HomeWidget"）。
            if (container is FrameworkElement fe)
            {
                var t = fe.TryFindResource(key) as System.Windows.DataTemplate;
                if (t != null) return t;
            }
            return System.Windows.Application.Current?.TryFindResource(key) as System.Windows.DataTemplate;
        }
    }

}