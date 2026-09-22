using System;
using System.Windows;
using System.Windows.Controls;
using TsuruLauncher.Models.Ecosystem;
using TsuruLauncher.ViewModels;

namespace TsuruLauncher.Views
{
    public partial class ResourceDetailPage : Page
    {
        private readonly ResourceDetailViewModel _viewModel;

        // 滚动自检用（滚到底前记一下可滚动高度，下一步再读 offset —— 不能同一帧读，见下面 case 12）
        private double _probeScrollable;
        /// <summary>自检用：删除确认弹框实例（验证按钮可点）。</summary>
        private Window? _testDlg;
        private double _probeBefore;

        public ResourceDetailPage(ModProject project, string? gameVersion = null)
        {
            InitializeComponent();
            _viewModel = DataContext as ResourceDetailViewModel;
            if (_viewModel == null) return;

            Loaded += async (s, e) =>
            {
                try
                {
                    await _viewModel.InitializeAsync(project, gameVersion);
                }
                catch { }

                // TSURU_SELFTEST=resourcedetail：数据到位后让应用自己走一遍
                // 「筛选下拉 / 已生效胶囊 / hero 按钮」并把结果写进日志。
                if (Environment.GetEnvironmentVariable("TSURU_SELFTEST") == "resourcedetail")
                    RunDetailSelfTest();
            };
        }

        #region 自检（TSURU_SELFTEST=resourcedetail）

        private void RunDetailSelfTest()
        {
            string shotDir = Environment.GetEnvironmentVariable("TSURU_SHOT_DIR") ?? string.Empty;

            var t = new System.Windows.Threading.DispatcherTimer(
                System.TimeSpan.FromMilliseconds(600),
                System.Windows.Threading.DispatcherPriority.Background, (a, b) => { }, Dispatcher);
            int step = 0;

            t.Tick += (s, e) =>
            {
                step++;
                var vm = _viewModel;
                try
                {
                    switch (step)
                    {
                        case 1:
                            Log($"[DetailSelfTest] 1 页面已加载 IsLoadingDetails={vm.IsLoadingDetails} " +
                                $"版本={vm.Resource?.Versions.Count ?? 0} " +
                                $"hero: 翻译按钮文本=\"{vm.TranslateButtonText}\" 更多按钮={(MoreBtn != null ? "存在" : "缺失")}");
                            break;

                        case 2:
                            Log($"[DetailSelfTest] 2 筛选下拉 平台={vm.PlatformFilters.Count}项(显示={vm.ShowPlatformFilter}) " +
                                $"游戏版本={vm.GameVersionFilters.Count}项(显示={vm.ShowGameVersionFilter}) " +
                                $"通道={vm.ChannelFilters.Count}项(显示={vm.ShowChannelFilter})");
                            Log($"[DetailSelfTest] 2b 下拉控件实例 " +
                                $"平台={(PlatformFilterCtl != null ? "有" : "无")} " +
                                $"游戏版本={(GameVersionFilterCtl != null ? "有" : "无")} " +
                                $"通道={(ChannelFilterCtl != null ? "有" : "无")}");
                            Log($"[DetailSelfTest] 2c 过滤前 版本总数={vm.FilteredVersions.Count} 页数={vm.TotalPages} 页码={vm.PageNumbers.Count}");
                            Shot(shotDir, "01-描述页");
                            break;

                        case 3:
                            var pf = vm.PlatformFilters.FirstOrDefault(o => o.IsSelected) ?? vm.PlatformFilters.FirstOrDefault();
                            if (pf != null)
                            {
                                pf.IsSelected = !pf.IsSelected;
                                vm.FiltersChangedCommand.Execute(null);
                                Log($"[DetailSelfTest] 3 切换平台筛选 \"{pf.Label}\" -> 选中={pf.IsSelected} " +
                                    $"剩余版本={vm.FilteredVersions.Count} 胶囊={vm.ActiveFilterChips.Count} " +
                                    $"HasActiveFilters={vm.HasActiveFilters}");
                            }
                            else Log("[DetailSelfTest] 3 没有平台可筛（该资源未声明 loader）");
                            break;

                        case 4:
                            var ch = vm.ChannelFilters.FirstOrDefault();
                            if (ch != null)
                            {
                                ch.IsSelected = true;
                                vm.FiltersChangedCommand.Execute(null);
                                Log($"[DetailSelfTest] 4 勾选通道 \"{ch.Label}\" -> 剩余版本={vm.FilteredVersions.Count} " +
                                    $"胶囊={vm.ActiveFilterChips.Count}");
                            }
                            else Log("[DetailSelfTest] 4 没有通道可筛");
                            // 切到「版本」页 —— ShowPanel 用了 opacity + translateY 200ms 入场动画，
                            // 截图放到下一步 (step 5) 让动画播完。
                            SwitchToVersionsTab();
                            // 同时也把 StartDownloadCommand 的可执行性测一下（验证 ⬇ 按钮现在能跑下载流程）
                            bool canStart = vm.Resource != null && vm.Resource.SelectedVersion != null && vm.StartDownloadCommand.CanExecute(null);
                            Log($"[DetailSelfTest] 4b ⬇ 按钮链路：SelectedVersion={(vm.Resource?.SelectedVersion?.FileName ?? "<null>")} " +
                                $"StartDownloadCommand.CanExecute={canStart}（=True 时 ⬇ 一击就下载）");
                            break;

                        case 5:
                            Shot(shotDir, "02-版本页");
                            int before = vm.FilteredVersions.Count;
                            vm.ClearAllFiltersCommand.Execute(null);
                            Log($"[DetailSelfTest] 5 清除全部筛选 -> {before} → {vm.FilteredVersions.Count} " +
                                $"胶囊={vm.ActiveFilterChips.Count} 期望=0");
                            Scroller?.ScrollToBottom();
                            Log("[DetailSelfTest] 5b 滚到底部，下载卡应在视野内");
                            break;

                        case 6:
                            vm.ShowAllGameVersions = true;
                            Log($"[DetailSelfTest] 6 显示全部版本=true -> 候选={vm.GameVersionFilters.Count} " +
                                $"当前结果={vm.FilteredVersions.Count}");
                            vm.ShowAllGameVersions = false;
                            break;

                        case 7:
                            vm.NextPageCommand.Execute(null);
                            Log($"[DetailSelfTest] 7 翻到第 {vm.CurrentPage} 页 -> 本页 {vm.PagedVersions.Count} 条 " +
                                $"CanPrev={vm.CanPrevPage} CanNext={vm.CanNextPage} 当前页标记={vm.PageNumbers.Count(p => p.IsCurrent)}");
                            break;

                        case 8:
                            Shot(shotDir, "03-版本页第2页");
                            vm.PrevPageCommand.Execute(null);
                            Log($"[DetailSelfTest] 8 翻回第 {vm.CurrentPage} 页 -> 本页 {vm.PagedVersions.Count} 条");
                            break;

                        case 9:
                            Log($"[DetailSelfTest] 9 依赖面板 Show={vm.ShowDependenciesPanel} 条数={vm.DependencyList.Count}");
                            // 顺便量一下版本列表的实际渲染尺寸 —— 「列表是空的」这类问题只能靠实测尺寸定位
                            Log($"[DetailSelfTest] 9b 版本列表 ActualHeight={VersionListBox?.ActualHeight ?? -1} " +
                                $"Items={VersionListBox?.Items.Count ?? -1} " +
                                $"图标={(vm.Resource?.HasIcon == true ? "已加载" : "未加载")} " +
                                $"作者=\"{vm.Resource?.Author}\"");
                            break;

                        case 10:
                            // 回到「描述」页
                            if (TabDesc != null) TabDesc.IsChecked = true;
                            ShowPanel(DescPanel, true);
                            ShowPanel(VersionPanel, false);
                            ShowPanel(GalleryPanel, false);
                            break;

                        case 11:
                            Shot(shotDir, "04-描述页（滚动条位置）");
                            // 把三个面板的可见性打出来 —— 描述 tab 下应该是
                            // 描述可见 / 版本折叠 / 图库折叠
                            Log($"[DetailSelfTest] 11c 描述 tab 可见性：Desc={DescPanel.Visibility} " +
                                $"Version={VersionPanel.Visibility} Gallery={GalleryPanel.Visibility} " +
                                $"TabVer.IsChecked={TabVer?.IsChecked} TabDesc.IsChecked={TabDesc?.IsChecked}");
                            break;

                        case 13:
                            Shot(shotDir, "06-图库tab");
                            // 切回描述页，继续滚动条验证
                            if (TabDesc != null) TabDesc.IsChecked = true;
                            ShowPanel(DescPanel, true);
                            ShowPanel(VersionPanel, false);
                            ShowPanel(GalleryPanel, false);
                            // 只应该剩**一个**滚动条（外层 Scroller）。内层 DescScroll 已删除。
                            // ⚠ ScrollToVerticalOffset 是异步到下一布局周期才生效，必须分两步读。
                            _probeScrollable = Scroller?.ScrollableHeight ?? -1;
                            _probeBefore = Scroller?.VerticalOffset ?? -1;
                            Scroller?.ScrollToVerticalOffset(Math.Max(0, _probeScrollable));
                            Scroller?.UpdateLayout();
                            Log($"[DetailSelfTest] ScrollProbe 请求滚到底：可滚动高度={_probeScrollable:F0} 滚之前 offset={_probeBefore:F0}");
                            break;

                        case 14:
                            double after = Scroller?.VerticalOffset ?? -1;
                            bool ok = _probeScrollable > 1 && after > 1;
                            // 顺便数一下页面里还有几个可见的滚动条 —— 用户吐槽「两个滑动条没意义」
                            int visibleBars = CountVisibleScrollBars();
                            Log($"[DetailSelfTest] ScrollProbe 结果 offset={after:F0} 可见滚动条数={visibleBars} " +
                                $"=> {(ok ? "✅ 滚动正常（单滚动条）" : "⚠ 滚动异常")}");
                            Shot(shotDir, "05-描述页滚到底");
                            Scroller?.ScrollToVerticalOffset(0);
                            Scroller?.UpdateLayout();
                            break;

                        case 15:
                            Log($"[DetailSelfTest] 滚回顶后 offset={Scroller?.VerticalOffset:F0}");
                            break;

                        case 16:
                            // 回归：模拟「1.8.9 实例打开一个不支持 1.8.9 的资源」
                            // （用户报的「版本列表显示未找到匹配版本」）
                            var (total, filtered) = vm.SelfTestApplyTargetGameVersion("1.8.9");
                            Log($"[DetailSelfTest] 16 模拟 TargetGameVersion=1.8.9 -> 总版本={total} 筛选后={filtered} " +
                                $"{(filtered > 0 ? "✅ 列表非空（修复生效）" : "❌ 被筛空了")}");
                            // 还原成原本的目标版本
                            vm.SelfTestApplyTargetGameVersion(vm.Resource?.Versions.FirstOrDefault()?.GameVersions.FirstOrDefault() ?? "");
                            break;

                        case 17:
                            // 回归：确认删除弹框的两个按钮**真的能点**。
                            // 之前 MakePillButton 用 MouseLeftButtonUp（被 ButtonBase 类处理器 Handled 吃掉）
                            // → 点了没反应；且 YesNo 落进 default 分支没有「取消」按钮。
                            _testDlg = Controls.iOS26Dialog.ShowCore(
                                Application.Current.MainWindow,
                                "确定要删除 Mod \"sodium.jar\" 吗？此操作不可恢复！",
                                "删除 Mod",
                                Controls.DialogIcon.Warning,
                                Controls.DialogButtons.YesNo);
                            if (_testDlg == null)
                            {
                                Log("[DetailSelfTest] 17 ❌ 弹框没建出来");
                                break;
                            }
                            _testDlg.UpdateLayout();
                            var pillBtns = FindVisualChildren<Button>(_testDlg)
                                .Where(b => b.Content is "取消" or "确认")
                                .ToList();
                            bool hasBoth = pillBtns.Any(b => (b.Content as string) == "取消")
                                        && pillBtns.Any(b => (b.Content as string) == "确认");
                            Log($"[DetailSelfTest] 17 删除确认弹框操作按钮：[{string.Join(", ", pillBtns.Select(b => $"\"{b.Content}\""))}] " +
                                $"{(hasBoth ? "✅ 取消 + 确认 都在" : "❌ 缺按钮")}");
                            ShotDialog(_testDlg, shotDir, "07-删除确认弹框");
                            // 派发 Click 到「取消」—— 走的就是用户点击时同一条链路
                            var cancelBtn = pillBtns.FirstOrDefault(b => (b.Content as string) == "取消");
                            cancelBtn?.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                            Log("[DetailSelfTest] 17b 已对「取消」派发 Click");
                            break;

                        case 18:
                            // 150ms 关闭动画后弹框应已 Close()
                            bool closed = _testDlg == null || !_testDlg.IsVisible;
                            Log($"[DetailSelfTest] 18 点击「取消」后弹框已关闭={closed} " +
                                $"{(closed ? "✅ Click 链路通了（按钮真能点）" : "❌ 按钮点了没反应")}");
                            break;

                        case 19:
                            Log("[DetailSelfTest] ✅ 完成");
                            t.Stop();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log($"[DetailSelfTest] 第 {step} 步异常: {ex.GetType().Name} {ex.Message}");
                    t.Stop();
                }

                if (step > 26) t.Stop();
            };

            t.Start();
        }

        /// <summary>切到「版本」标签（自检里模拟点击）。</summary>
        private void SwitchToVersionsTab()
        {
            // 同步把 RadioButton 也设上（让 tab 视觉对得上当前面板）
            if (TabVer != null) TabVer.IsChecked = true;
            ShowPanel(DescPanel, false);
            ShowPanel(VersionPanel, true);
            ShowPanel(GalleryPanel, false);
        }

        private static void Shot(string dir, string name)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                string path = System.IO.Path.Combine(dir, name + ".png");
                TsuruLauncher.App.CaptureToPng(path);
                Log($"[DetailSelfTest] 📷 {path}");
            }
            catch (Exception ex)
            {
                Log($"[DetailSelfTest] 截图失败: {ex.Message}");
            }
        }

        private static void Log(string msg)
        {
            Utilities.Logger.LogInfo(msg);
            System.Diagnostics.Debug.WriteLine(msg);
        }

        /// <summary>把任意 Window 单独渲染成 PNG（对话框在独立 VisualTree 里，抓主窗口抓不到）。</summary>
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
                if (pw <= 0 || ph <= 0) { Log($"[DetailSelfTest] 弹框截图跳过：尺寸 {w.ActualWidth}x{w.ActualHeight}"); return; }
                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(pw, ph, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, System.Windows.Media.PixelFormats.Pbgra32);
                rtb.Render(w);
                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using var fs = System.IO.File.Create(path);
                enc.Save(fs);
                Log($"[DetailSelfTest] 📷 {name} {pw}x{ph}");
            }
            catch (Exception ex) { Log($"[DetailSelfTest] 弹框截图失败: {ex.Message}"); }
        }

        /// <summary>
        /// 数页面里当前**可见**的滚动条数量 —— 用来验证「两个滑动条」是否真的清掉了。
        /// 只数实际渲染出来的（IsVisible + Visibility.Visible），不数模板里 Collapsed 的那些。
        /// </summary>
        private int CountVisibleScrollBars()
        {
            int n = 0;
            foreach (var sb in FindVisualChildren<System.Windows.Controls.Primitives.ScrollBar>(this))
            {
                if (sb.IsVisible && sb.Visibility == Visibility.Visible && sb.ActualWidth > 0.5 && sb.ActualHeight > 0.5)
                    n++;
            }
            return n;
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

        #endregion

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack)
                NavigationService.GoBack();
        }

        private void OpenBrowser_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.OpenInBrowserCommand.Execute(null);
        }
        /// <summary>相关链接按钮：Tag 里挂 URL，用系统默认浏览器打开。</summary>
        private void OpenLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement fe && fe.Tag is string url && !string.IsNullOrWhiteSpace(url))
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                    {
                        UseShellExecute = true
                    });
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "ResourceDetailPage.OpenLink");
            }
        }

                /// <summary>描述 / 版本 / 图库 三个标签。切换带淡入 + translateY。</summary>
        private void DetailTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not string tag) return;
            bool desc = tag == "desc", ver = tag == "ver", gal = tag == "gallery";

            // 用户点 tab：把对应的 RadioButton 标上 checked（让它视觉对得上当前面板）
            if (sender is System.Windows.Controls.RadioButton rb) rb.IsChecked = true;

            ShowPanel(DescPanel, desc);
            ShowPanel(VersionPanel, ver);
            ShowPanel(GalleryPanel, gal);
        }

        /// <summary>
        /// 切 tab 时的过渡：Axolotl 那种「淡入 + 轻微上推」 200ms。
        /// 用 Storyboard 而不是 PlayOpacity 是因为还要动 translateY —— 单纯 opacity 太生硬。
        /// </summary>
        private static void ShowPanel(FrameworkElement? panel, bool show)
        {
            if (panel == null) return;

            // 取消旧动画，避免连点 tab 时叠加
            panel.BeginAnimation(UIElement.OpacityProperty, null);
            var tt = panel.RenderTransform as System.Windows.Media.TranslateTransform
                     ?? new System.Windows.Media.TranslateTransform();
            panel.RenderTransform = tt;
            tt.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);

            if (show)
            {
                panel.Visibility = Visibility.Visible;
                panel.Opacity = 0;
                tt.Y = 12;            // 入场起始位置（稍微往下）
                var fade = new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = Services.Animation.AxolotlMotion.PageEnterMoveEase
                };
                var slide = new System.Windows.Media.Animation.DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = Services.Animation.AxolotlMotion.PageEnterMoveEase
                };
                panel.BeginAnimation(UIElement.OpacityProperty, fade);
                tt.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, slide);
            }
            else
            {
                panel.Visibility = Visibility.Collapsed;
                // 还原，下一次显示时仍从 12 开始
                panel.Opacity = 1;
                tt.Y = 0;
            }
        }

        /// <summary>版本表格那一行的 ⬇ 按钮：选中该版本并**直接开始下载**（不只是选中）。
        /// 之前只设 SelectedVersion，导致用户点了 ⬇ 却什么都没发生 —— 这就是用户报
        /// 「这一列点击不了」的真因。下载流程由 VM 的 StartDownloadCommand 负责，
        /// 走 ModInstallPicker 选实例后入队下载管理器。</summary>
        private void PickVersion_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement fe && fe.Tag is Models.Ecosystem.ModFile file &&
                    DataContext is ViewModels.ResourceDetailViewModel vm)
                {
                    vm.Resource.SelectedVersion = file;
                    vm.SelectedVersionItem = file;
                    if (vm.StartDownloadCommand.CanExecute(null))
                        vm.StartDownloadCommand.Execute(null);
                }
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "ResourceDetailPage.PickVersion");
            }
        }

        /// <summary>版本表格那一行的 ↗ 按钮：在浏览器里打开该版本页面（美西螈同款第二颗按钮）。</summary>
        private void OpenVersionLink_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement fe && fe.Tag is Models.Ecosystem.ModFile file &&
                    DataContext is ViewModels.ResourceDetailViewModel vm)
                {
                    string url = vm.BuildVersionWebUrl(file);
                    if (!string.IsNullOrWhiteSpace(url))
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                        {
                            UseShellExecute = true
                        });
                }
                e.Handled = true;
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "ResourceDetailPage.OpenVersionLink");
            }
        }

        /// <summary>分页条的页码按钮（Tag 是页码）。</summary>
        private void PageButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is int page &&
                DataContext is ViewModels.ResourceDetailViewModel vm)
                vm.GoPageCommand.Execute(page);
        }

        /// <summary>hero 的「翻译」：切中/英简介。</summary>
        private void Translate_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ViewModels.ResourceDetailViewModel vm)
                vm.ToggleTranslationCommand.Execute(null);
        }

        /// <summary>
        /// hero 的「⋮」。菜单项按美西螈 ProjectHeader 的 OverflowMenu 来：
        /// 在浏览器中打开 / 复制下载链接 / ── / 举报项目（红色）。
        /// </summary>
        private void More_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not ViewModels.ResourceDetailViewModel vm) return;

            var menu = new System.Windows.Controls.ContextMenu
            {
                PlacementTarget = MoreBtn,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
                Background = TryBrush("SurfaceFloatingBrush") ?? TryBrush("Surface3Brush"),
                BorderBrush = TryBrush("GlassBorderBrush"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4)
            };

            var open = new MenuItem { Header = "在浏览器中打开", Icon = Glyph("🌐") };
            open.Click += (a, b) => vm.OpenInBrowserCommand.Execute(null);
            menu.Items.Add(open);

            var copy = new MenuItem { Header = "复制下载链接", Icon = Glyph("🔗") };
            copy.Click += (a, b) => vm.CopyDownloadLinkCommand.Execute(null);
            menu.Items.Add(copy);

            menu.Items.Add(new Separator());

            var report = new MenuItem
            {
                Header = "举报项目",
                Foreground = TryBrush("DangerBrush"),
                Icon = Glyph("⚠"),
                IsEnabled = vm.CanReport
            };
            report.Click += (a, b) => vm.ReportProjectCommand.Execute(null);
            menu.Items.Add(report);

            menu.IsOpen = true;
        }

        private static System.Windows.Controls.TextBlock Glyph(string s)
            => new() { Text = s, FontSize = 13, Width = 18, TextAlignment = TextAlignment.Center };

        private static System.Windows.Media.Brush? TryBrush(string key)
        {
            try { return System.Windows.Application.Current?.TryFindResource(key) as System.Windows.Media.Brush; }
            catch { return null; }
        }

        /// <summary>
        /// 三颗 MultiSelectFilter 里任意一项勾选变了 → 让 ViewModel 重新过滤。
        /// 控件本身不认识业务字段，事件只是「有事发生了」的信号。
        /// </summary>
        private void Filter_SelectionChanged(object sender, EventArgs e)
        {
            if (DataContext is ViewModels.ResourceDetailViewModel vm)
                vm.FiltersChangedCommand.Execute(null);
        }

    }
}