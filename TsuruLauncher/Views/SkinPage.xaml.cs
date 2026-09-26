using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TsuruLauncher.Services.Animation;
using TsuruLauncher.ViewModels;

namespace TsuruLauncher.Views
{
    /// <summary>
    /// 皮肤选择器。
    /// 左侧 3D 预览（拖动旋转）、右侧本地皮肤库（点击预览 / 悬停删除 / 拖放添加）。
    /// </summary>
    public partial class SkinPage : Page
    {
        public SkinPage()
        {
            InitializeComponent();

            // 脚下投影跟着皮肤一起淡入（没皮肤时不显示）
            DataContextChanged += (_, __) =>
            {
                if (DataContext is not SkinViewModel vm) return;

                vm.PropertyChanged += (s, e) =>
                {
                    switch (e.PropertyName)
                    {
                        case nameof(SkinViewModel.PreviewSkin):
                            FadeShadow(vm.PreviewSkin != null);
                            break;

                        // ⚠ 文案变化也做淡入，不要"啪"一下换字
                        case nameof(SkinViewModel.ChipText):
                            PulseIn(ChipTextBlock);
                            break;

                        case nameof(SkinViewModel.ApplyStatus):
                            PulseIn(ApplyStatusText);
                            break;

                        case nameof(SkinViewModel.SectionTitle):
                            PulseIn(SectionTitleText);
                            break;
                    }
                };
            };

            Loaded += (_, __) =>
            {
                if (Vm != null) FadeShadow(Vm.PreviewSkin != null, animate: false);

                // 把「取当前账号」的方式交给 VM —— SkinPage 的 DataContext 是 SkinViewModel，
                // 拿不到 MainViewModel，所以从宿主窗口那边取。
                // 传委托而不是快照：这样用户登录完再切到「官方皮肤」就能拿到最新账号。
                // 每次页面显示都检查一下账号有没有换过
                IsVisibleChanged += (_, e) => { if (e.NewValue is true) Vm?.OnPageShown(); };

                // ⚠⚠ 光靠 IsVisibleChanged 不够 ——
                //   用户可能**在皮肤页里直接切账号**（右侧账号面板），这时页面一直可见、
                //   IsVisibleChanged 不触发，皮肤页就会继续显示上一个账号的状态。
                //   `AccountManager.CurrentAccount` 是 [ObservableProperty]，
                //   切账号只会发 PropertyChanged（**不发 AccountsChanged**），所以监听它。
                if (Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel mainVm)
                {
                    mainVm.AccountManager.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName == nameof(Services.AccountManager.CurrentAccount))
                            Vm?.OnPageShown();
                    };
                }

                Vm?.SetAccountProvider(() =>
                    (Application.Current.MainWindow?.DataContext as ViewModels.MainViewModel)
                        ?.AccountManager.CurrentAccount);
            };

            if (Environment.GetEnvironmentVariable("TSURU_SELFTEST") == "skin")
                Loaded += async (_, __) =>
                {
                    await System.Threading.Tasks.Task.Delay(2500).ConfigureAwait(true);
                    Dispatcher.BeginInvoke(RunSkinSelfTest, System.Windows.Threading.DispatcherPriority.Background);
                };
        }

        /// <summary>脚下投影的淡入淡出（和换皮肤的淡入同步，不做硬切）。</summary>
        private void FadeShadow(bool show, bool animate = true)
        {
            try
            {
                double to = show ? 0.9 : 0;
                if (!animate || !PageTransition.AnimationsEnabled)
                {
                    GroundShadow.BeginAnimation(UIElement.OpacityProperty, null);
                    GroundShadow.Opacity = to;
                    return;
                }
                PageTransition.PlayOpacity(GroundShadow, to, 260, AxolotlMotion.EaseOut);
            }
            catch { }
        }

        // ── 自检（TSURU_SELFTEST=skin）────────────────────────────
        private void RunSkinSelfTest()
        {
            string shotDir = Environment.GetEnvironmentVariable("TSURU_SHOT_DIR") ?? string.Empty;
            var t = new System.Windows.Threading.DispatcherTimer(
                TimeSpan.FromMilliseconds(1600), System.Windows.Threading.DispatcherPriority.Background,
                (a, b) => { }, Dispatcher);
            int step = 0;

            t.Tick += (s, e) =>
            {
                try
                {
                    if (step == 0)
                    {
                        Log($"[SkinSelfTest] 皮肤库 {Vm?.SavedSkins.Count ?? -1} 张，当前预览=\"{Vm?.PreviewName}\"");
                        Log($"[SkinSelfTest] 指示块 Left={IndicatorX():F1}（已保存皮肤选中）");

                        // 拖拽逻辑现在在网页里（index.html 的 pointermove 只用 clientX），
                        // 这里只确认网页加载完了
                        Log($"[SkinSelfTest] 预览控件 = {Preview.GetType().Name}");

                        // 缩略图缓存对照：连做两次 Reload，第二次应该几乎全命中
                        Vm.Reload();
                        Vm.Reload();

                        // 缩略图缓存对照：连做两次 Reload，第二次应该几乎全命中
                        Vm.Reload();
                        Vm.Reload();
                        TabOfficial.IsChecked = true;
                    }
                    else if (step == 1)
                    {
                        Log($"[SkinSelfTest] 切到官方皮肤：指示块 Left={IndicatorX():F1} " +
                            $"IsOfficialTab={Vm?.IsOfficialTab} " +
                            $"{(IndicatorX() > 1 ? "✅ 滑动生效" : "❌ 没动")}");
                        LogOfficialState("第 1 拍");
                    }
                    else if (step == 2)
                    {
                        LogOfficialState("第 2 拍");
                    }
                    else if (step == 3)
                    {
                        LogOfficialState("第 3 拍");
                    }
                    else if (step == 4)
                    {
                        LogOfficialState("第 4 拍");
                        Shot(shotDir, "01-官方皮肤");
                        // WebView2 的内容普通截图抓不到，让网页自己导出
                        _ = ExportViewsAsync(shotDir);
                        TabSaved.IsChecked = true;
                    }
                    else
                    {
                        Log($"[SkinSelfTest] 切回已保存：指示块 Left={IndicatorX():F1} " +
                            $"IsOfficialTab={Vm?.IsOfficialTab} 预览=\"{Vm?.PreviewName}\"");
                        Shot(shotDir, "02-已保存皮肤");
                        Log("[SkinSelfTest] ✅ 完成");
                        t.Stop();
                    }
                    step++;
                }
                catch (Exception ex)
                {
                    Log($"[SkinSelfTest] 异常: {ex.GetType().Name} {ex.Message}");
                    t.Stop();
                }
            };
            t.Start();
        }

        /// <summary>自检用：导出正面 / 背面两个视角（背面用来确认披风）。</summary>
        private async Task ExportViewsAsync(string dir)
        {
            bool ok = await Preview.ExportPngAsync(System.IO.Path.Combine(dir, "10-网页预览-正面.png"));
            Log($"[SkinSelfTest] 正面导出 {(ok ? "成功" : "失败")} 就绪={Preview.IsReady}");

            await Preview.SetYawAsync(180);
            await Task.Delay(400);
            bool ok2 = await Preview.ExportPngAsync(System.IO.Path.Combine(dir, "11-网页预览-背面.png"));
            Log($"[SkinSelfTest] 背面导出 {(ok2 ? "成功" : "失败")}");

            await Preview.SetYawAsync(15.75);
        }

        /// <summary>自检用：打印官方皮肤的加载状态。</summary>
        private void LogOfficialState(string when)
        {
            var vm = Vm;
            if (vm == null) { Log($"[SkinSelfTest] {when} 官方皮肤：VM 为空"); return; }

            string account = "(未登录)";
            try
            {
                var a = (Application.Current.MainWindow?.DataContext as ViewModels.MainViewModel)
                        ?.AccountManager.CurrentAccount;
                if (a != null)
                    account = $"{a.Username}/{a.Type}/令牌{(string.IsNullOrEmpty(a.MinecraftAccessToken) ? "空" : "有")}";
            }
            catch { }

            Log($"[SkinSelfTest] {when} 官方皮肤：账号={account} " +
                $"加载中={vm.IsOfficialLoading} 已拿到={vm.HasOfficialSkin} " +
                $"模型=\"{vm.OfficialVariant}\" 预览=\"{vm.PreviewName}\" " +
                $"{(string.IsNullOrEmpty(vm.OfficialMessage) ? "" : "原因=" + vm.OfficialMessage.Replace("\n", " | "))}");
        }

        /// <summary>读指示块当前的视觉 X（Canvas.Left + TranslateX）。</summary>
        private double IndicatorX()
        {
            if (SkinTabIndicator == null) return double.NaN;
            double left = Canvas.GetLeft(SkinTabIndicator);
            if (double.IsNaN(left)) left = 0;
            double tx = SkinTabIndicator.RenderTransform is TranslateTransform tt ? tt.X : 0;
            return left + tx;
        }

        private static void Shot(string dir, string name)
        {
            if (string.IsNullOrWhiteSpace(dir)) return;
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                TsuruLauncher.App.CaptureToPng(System.IO.Path.Combine(dir, name + ".png"));
                Utilities.Logger.LogInfo($"[SkinSelfTest] 📷 {name}");
            }
            catch (Exception ex) { Utilities.Logger.LogInfo($"[SkinSelfTest] 截图失败: {ex.Message}"); }
        }

        private static void Log(string msg) => Utilities.Logger.LogInfo(msg);

        private SkinViewModel? Vm => DataContext as SkinViewModel;

        // ── 页签（已保存 / 官方）────────────────────────────────────

        private void SkinTab_Checked(object sender, RoutedEventArgs e)
        {
            if (Vm == null) return;

            bool toOfficial = ReferenceEquals(sender, TabOfficial);
            Vm.IsOfficialTab = toOfficial;
            CrossFadePanels(toOfficial);
        }

        /// <summary>
        /// 两个页签面板交叉淡入（淡出旧的 + 淡入新的 + 轻微上浮）。
        /// ⚠ 用 Opacity 而不是 Visibility —— 绑 Visibility 只能硬切。
        /// </summary>
        private void CrossFadePanels(bool toOfficial)
        {
            try
            {
                var show = toOfficial ? OfficialPanel : SavedPanel;
                var hide = toOfficial ? SavedPanel : OfficialPanel;

                show.IsHitTestVisible = true;
                hide.IsHitTestVisible = false;

                FadePanel(hide, 0, 0);
                FadePanel(show, 1, 10);
            }
            catch { }
        }

        private static void FadePanel(UIElement el, double to, double fromOffset)
        {
            if (el is not FrameworkElement fe) return;

            fe.RenderTransform = new TranslateTransform(0, fromOffset);
            var tt = (TranslateTransform)fe.RenderTransform;

            PageTransition.PlayOpacity(fe, to, 220, AxolotlMotion.EaseOut);

            // ⚠ 从 fromOffset 移到 0（写成 0→0 是没效果的）
            var anim = new System.Windows.Media.Animation.DoubleAnimation(
                fromOffset, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = AxolotlMotion.EaseOut,
            };
            tt.BeginAnimation(TranslateTransform.YProperty, anim);
        }

        // ── 皮肤卡片 ───────────────────────────────────────────────

        /// <summary>文案变了就淡入一次（代替硬切）。</summary>
        private static void PulseIn(FrameworkElement? el)
        {
            if (el == null) return;
            try
            {
                el.Opacity = 0;
                PageTransition.PlayOpacity(el, 1, 200, AxolotlMotion.EaseOut);
            }
            catch { }
        }

        /// <summary>卡片悬停上浮 —— 用 TranslateY 动画，不做硬切。</summary>
        private static void LiftCard(object sender, double toY)
        {
            if (sender is not FrameworkElement fe) return;
            try
            {
                if (fe.RenderTransform is not TranslateTransform tt)
                {
                    tt = new TranslateTransform();
                    fe.RenderTransform = tt;
                }

                var anim = new System.Windows.Media.Animation.DoubleAnimation(
                    tt.Y, toY, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = AxolotlMotion.EaseOut,
                };
                tt.BeginAnimation(TranslateTransform.YProperty, anim);
            }
            catch { }
        }

        private void SkinCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not SkinEntry entry) return;

            // 占位卡：走「添加皮肤」（文件选择器）
            if (entry.IsAddCard) { CreateSkin_MouseUp(sender, e); return; }

            // ⚠⚠ 必须走**同一条「应用」路径**（SelectDefaultAsync）：
            //   它内部会先弹确认框、再上传到账号。
            //   之前这里调的是只切预览的 Select()，结果点「已保存皮肤」里的卡片
            //   只改了 3D 预览、账号根本没换 —— 用户点了半天「切不回去」就是这个原因。
            _ = Vm?.SelectDefaultAsync(entry);
        }

        /// <summary>点「默认皮肤」卡片 —— 只是选中并预览（真正应用到账号要上传，另走一条路）。</summary>
        private void DefaultSkinCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is SkinEntry entry)
                _ = Vm?.SelectDefaultAsync(entry);
        }

        /// <summary>悬停时让「✕ 删除」淡入（不做硬切）。</summary>
        private void SkinCard_MouseEnter(object sender, MouseEventArgs e)
        {
            FadeDelete(sender, 1);
            LiftCard(sender, -3);      // 悬停轻微上浮
        }

        private void SkinCard_MouseLeave(object sender, MouseEventArgs e)
        {
            FadeDelete(sender, 0);
            LiftCard(sender, 0);
        }

        private static void FadeDelete(object sender, double to)
        {
            if (sender is not DependencyObject card) return;
            var btn = FindChild<Button>(card, b => (b.Content as string) == "✕");
            if (btn == null) return;

            btn.BeginAnimation(UIElement.OpacityProperty, null);
            btn.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(to, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = AxolotlMotion.EaseInOut
                });
        }

        private static T? FindChild<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
        {
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(root, i);
                if (c is T t && match(t)) return t;
                var deep = FindChild(c, match);
                if (deep != null) return deep;
            }
            return null;
        }

        private void DeleteSkin_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;   // 别让点击穿透到卡片（否则删完还会触发选中）
            if (sender is FrameworkElement fe && fe.Tag is SkinEntry entry)
                Vm?.Remove(entry);
        }

        // ── 创建 / 打开文件夹 ──────────────────────────────────────

        private void CreateSkin_Click(object sender, RoutedEventArgs e) => PickSkinFile();

        /// <summary>空状态那块大落点用的是 MouseLeftButtonUp（Border 没有 Click）。</summary>
        private void CreateSkin_MouseUp(object sender, MouseButtonEventArgs e) => PickSkinFile();

        private async void PickSkinFile()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "选择皮肤 PNG",
                    Filter = "Minecraft 皮肤 (*.png)|*.png",
                    Multiselect = true,
                };

                if (dlg.ShowDialog(Window.GetWindow(this)) != true) return;

                int ok = 0;
                foreach (var file in dlg.FileNames)
                    if (await Vm!.ImportAsync(file)) ok++;

                if (ok > 0)
                    Controls.iOS26Dialog.Show($"已添加 {ok} 张皮肤。", "皮肤", Controls.DialogIcon.Success);
            }
            catch (Exception ex)
            {
                Controls.iOS26Dialog.Show($"添加失败：{ex.Message}", "皮肤", Controls.DialogIcon.Error);
            }
        }

        private void EditSkin_Click(object sender, RoutedEventArgs e)
        {
            var entry = Vm?.SavedSkins.FirstOrDefault(s => s.IsSelected);
            if (entry == null)
            {
                Controls.iOS26Dialog.Show(
                    "还没有可编辑的皮肤。点右上角「创建皮肤」添加一张后再试。",
                    "皮肤", Controls.DialogIcon.Info);
                return;
            }

            try
            {
                // 用系统默认的图片编辑器打开这张 PNG —— 改完直接保存即生效
                // （页面每次进来都会重新读文件）。想找文件夹就编辑后另存。
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(entry.Path)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Controls.iOS26Dialog.Show(
                    $"打不开图片编辑器：{ex.Message}\n\n皮肤文件在：{Services.SkinService.SkinDirectory}",
                    "皮肤", Controls.DialogIcon.Error);
            }
        }

        // ── 拖放添加 ───────────────────────────────────────────────

        private void Page_DragOver(object sender, DragEventArgs e)
        {
            bool ok = e.Data.GetDataPresent(DataFormats.FileDrop)
                      && (e.Data.GetData(DataFormats.FileDrop) as string[])?.Any(IsPng) == true;

            e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;

            if (ok) Fade(DropOverlay, 1);
        }

        private void Page_DragLeave(object sender, DragEventArgs e) => Fade(DropOverlay, 0);

        // ⚠ async void 里抛异常会**直接崩掉整个应用**（没人接得住）—— 必须自己兜住
        private async void Page_Drop(object sender, DragEventArgs e)
        {
            try
            {
                Fade(DropOverlay, 0);

                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;

                int ok = 0;
                foreach (var f in files.Where(IsPng))
                    if (await Vm!.ImportAsync(f)) ok++;

                if (ok > 0)
                    Controls.iOS26Dialog.Show($"已添加 {ok} 张皮肤。", "皮肤", Controls.DialogIcon.Success);
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "拖放导入皮肤");
                Controls.iOS26Dialog.Show("导入失败：" + ex.Message, "皮肤", Controls.DialogIcon.Error);
            }
        }

        private static bool IsPng(string path)
            => path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

        /// <summary>拖放遮罩的淡入淡出（150ms，不做硬切）。</summary>
        private static void Fade(UIElement el, double to)
        {
            el.BeginAnimation(UIElement.OpacityProperty, null);
            el.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(to, TimeSpan.FromMilliseconds(150))
                {
                    EasingFunction = AxolotlMotion.EaseInOut
                });
        }
    }
}
