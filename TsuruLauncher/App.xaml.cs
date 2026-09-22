using System.Windows;
using TsuruLauncher.Controls;

namespace TsuruLauncher
{
    public partial class App : Application
    {
        public App()
        {
            // Global Exception Handling
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
            System.AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        }

        private void Application_Startup(object sender, StartupEventArgs e)
        {
            // TSURU_SOFTWARE_RENDER=1：强制软件渲染（用于截图 / 远程桌面等无 GPU 场景）
            try
            {
                if (Environment.GetEnvironmentVariable("TSURU_SOFTWARE_RENDER") == "1")
                {
                    System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
                }
            }
            catch { }

            TsuruLauncher.Utilities.Logger.Initialize();
            TsuruLauncher.Utilities.Logger.Log("Application Started");

            // Apply the saved appearance (mode + accent) before the first window renders
            try
            {
                var cfg = Services.ConfigService.Instance.Settings;
                Services.ThemeService.Apply(cfg.ThemeMode, cfg.AccentColor);
            }
            catch { }

            var mainWindow = new MainWindow();
            mainWindow.Show();

            // --page <tag>：启动后直接进入指定页面（home / resources / download / lab / settings）
            try
            {
                if (e.Args != null)
                {
                    int index = Array.FindIndex(e.Args, a => string.Equals(a, "--page", StringComparison.OrdinalIgnoreCase));
                    if (index >= 0 && index + 1 < e.Args.Length)
                    {
                        string tag = e.Args[index + 1].ToLowerInvariant().Trim();
                        mainWindow.Dispatcher.BeginInvoke(new Action(() => mainWindow.NavigateTo(tag)), System.Windows.Threading.DispatcherPriority.Loaded);
                        TsuruLauncher.Utilities.Logger.Log("[Startup] --page " + tag);
                    }
                }
            }
            catch (Exception ex)
            {
                TsuruLauncher.Utilities.Logger.LogError(ex, "App --page");
            }

            // --audit-ui：遍历可视树检查文字对比度（开发自检，配合浅色/深色主题排查看不清的文字）
            try
            {
                if (e.Args != null && e.Args.Any(a => string.Equals(a, "--audit-ui", StringComparison.OrdinalIgnoreCase)))
                {
                    mainWindow.Dispatcher.BeginInvoke(new Action(() => AuditUiContrast(mainWindow)),
                        System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }
            }
            catch { }

            // --log-window：启动后直接打开实时日志窗口（方便建快捷方式查看日志）
            try
            {
                if (e.Args != null && e.Args.Any(a => string.Equals(a, "--log-window", StringComparison.OrdinalIgnoreCase)))
                {
                    Services.LaunchLogHub.BeginSession("Tsuru Launcher");
                    Services.LaunchLogHub.Append(Services.LaunchLogKind.Status, "已通过 --log-window 打开日志窗口");
                    Views.LaunchLogWindow.ShowWindow("Tsuru Launcher");
                }
            }
            catch { }

            // --screenshot <path> [--screenshot-delay <ms>]：延时把主窗口渲染成 PNG。
            // 用途：无 GPU / 抢不到前台的场景下，让**应用自己**把界面渲染出来给开发看，
            // 而不是靠外部截屏（外部截屏要么抢不到前台，要么截到别的窗口）。
            // 配合 TSURU_SOFTWARE_RENDER=1 更稳。TSURU_SCREENSHOT_EXIT=1 可在截完后退。
            try
            {
                if (e.Args != null)
                {
                    int si = Array.FindIndex(e.Args, a => string.Equals(a, "--screenshot", StringComparison.OrdinalIgnoreCase));
                    if (si >= 0 && si + 1 < e.Args.Length)
                    {
                        string outPath = e.Args[si + 1];
                        int delayMs = 8000;
                        int di = Array.FindIndex(e.Args, a => string.Equals(a, "--screenshot-delay", StringComparison.OrdinalIgnoreCase));
                        if (di >= 0 && di + 1 < e.Args.Length && int.TryParse(e.Args[di + 1], out int d) && d > 0)
                            delayMs = d;

                        var shotTimer = new System.Windows.Threading.DispatcherTimer
                        {
                            Interval = TimeSpan.FromMilliseconds(delayMs)
                        };
                        shotTimer.Tick += (s2, e2) =>
                        {
                            shotTimer.Stop();
                            CaptureWindowToPng(mainWindow, outPath);
                            if (Environment.GetEnvironmentVariable("TSURU_SCREENSHOT_EXIT") == "1")
                                Shutdown();
                        };
                        shotTimer.Start();
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 把窗口渲染成 PNG。
        /// 先铺一层不透明底色再叠加窗口内容 —— 窗口用了 FluentWindow，直接渲染可能得到
        /// 半透明像素（PNG 看着一片黑），铺底之后所见即所得。
        /// </summary>
        /// <summary>把窗口渲染成 PNG。公开出来供页面自检调用（见 ResourceDetailPage.RunDetailSelfTest）。</summary>
        public static void CaptureToPng(string path)
        {
            if (Application.Current?.MainWindow is Window w) CaptureWindowToPng(w, path);
        }

        private static void CaptureWindowToPng(Window w, string path)
        {
            try
            {
                var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(w);                int pw = (int)Math.Ceiling(w.ActualWidth * dpi.DpiScaleX);
                int ph = (int)Math.Ceiling(w.ActualHeight * dpi.DpiScaleY);
                if (pw <= 0 || ph <= 0)
                {
                    TsuruLauncher.Utilities.Logger.LogWarning($"[Screenshot] 窗口尺寸为 0，跳过（{w.ActualWidth}x{w.ActualHeight}）");
                    return;
                }

                var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    pw, ph, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, System.Windows.Media.PixelFormats.Pbgra32);

                var bg = Application.Current?.TryFindResource("BackgroundBrush") as System.Windows.Media.Brush
                         ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF4, 0xF6, 0xF9));

                var dv = new System.Windows.Media.DrawingVisual();
                using (var dc = dv.RenderOpen())
                    dc.DrawRectangle(bg, null, new Rect(0, 0, w.ActualWidth, w.ActualHeight));
                rtb.Render(dv);

                rtb.Render(w);

                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                using var fs = System.IO.File.Create(path);
                enc.Save(fs);

                TsuruLauncher.Utilities.Logger.LogInfo($"[Screenshot] {pw}x{ph} -> {path}");
            }
            catch (Exception ex)
            {
                TsuruLauncher.Utilities.Logger.LogError(ex, "CaptureWindowToPng");
            }
        }

        /// <summary>
        /// 对比度自检：把界面上每个有文字的 TextBlock 的前景色与最近的不透明背景色做 WCAG 对比度计算，
        /// 低于 3.0 的全部写进日志，用来发现"浅色模式下白字白底"这类问题。
        /// </summary>
        private void AuditUiContrast(Window window)
        {
            try
            {
                int checkedCount = 0, lowCount = 0;
                foreach (var tb in FindVisualChildren<System.Windows.Controls.TextBlock>(window))
                {
                    string text = (tb.Text ?? string.Empty).Trim();
                    if (text.Length == 0 || !tb.IsVisible) continue;

                    var fgBrush = tb.Foreground as System.Windows.Media.SolidColorBrush;
                    if (fgBrush == null) continue;

                    var bgColor = FindBackgroundColor(tb) ?? System.Windows.Media.Color.FromRgb(0xF4, 0xF6, 0xF9);
                    double ratio = ContrastRatio(fgBrush.Color, bgColor);
                    checkedCount++;

                    if (ratio < 3.0)
                    {
                        lowCount++;
                        string preview = text.Length > 22 ? text.Substring(0, 22) : text;
                        TsuruLauncher.Utilities.Logger.Log("[Audit] contrast=" + ratio.ToString("F2") +
                            " fg=#" + fgBrush.Color.ToString().Substring(3) + " bg=#" + bgColor.ToString().Substring(3) +
                            " text=\"" + preview + "\"");
                    }
                }
                TsuruLauncher.Utilities.Logger.Log("[Audit] done checked=" + checkedCount + " low=" + lowCount);
            }
            catch (Exception ex)
            {
                TsuruLauncher.Utilities.Logger.LogError(ex, "AuditUiContrast");
            }
        }

        private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(System.Windows.DependencyObject root)
            where T : System.Windows.DependencyObject
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T typed) yield return typed;
                foreach (var nested in FindVisualChildren<T>(child)) yield return nested;
            }
        }

        private static System.Windows.Media.Color? FindBackgroundColor(System.Windows.DependencyObject element)
        {
            var current = element;
            while (current != null)
            {
                System.Windows.Media.Brush? brush = null;
                if (current is System.Windows.Controls.Panel panel) brush = panel.Background;
                else if (current is System.Windows.Controls.Border border) brush = border.Background;
                else if (current is System.Windows.Controls.Control control) brush = control.Background;

                if (brush is System.Windows.Media.SolidColorBrush solid && solid.Color.A == 255) return solid.Color;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static double ContrastRatio(System.Windows.Media.Color a, System.Windows.Media.Color b)
        {
            double la = RelativeLuminance(a), lb = RelativeLuminance(b);
            double lighter = Math.Max(la, lb), darker = Math.Min(la, lb);
            return (lighter + 0.05) / (darker + 0.05);
        }

        private static double RelativeLuminance(System.Windows.Media.Color c)
        {
            double Channel(byte v)
            {
                double s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        /// <summary>0x88980406 = UCEERR_RENDERTHREADFAILURE（WPF 渲染线程失效）。</summary>
        private const int UCEERR_RENDERTHREADFAILURE = unchecked((int)0x88980406);

        private static int _handlingDispatcherException;
        private static int _dispatcherExceptionCount;
        private static bool _renderThreadFailed;

        /// <summary>
        /// 全局 UI 异常兜底。
        ///
        /// **这就是"点 ▦ 卡死"的最后一环**：旧实现在这里**每次都**调用
        /// <c>iOS26Dialog.Show(...)</c> → <c>Window.ShowDialog()</c>
        /// （模态 + Topmost + 嵌套消息泵），而且没有任何重入保护。
        /// 现场日志里 <c>UCEERR_RENDERTHREADFAILURE (0x88980406)</c> 在**同一秒内出现了 30 次**
        /// —— 于是 30 层嵌套模态被层层压上；渲染线程又已经死了、窗口根本画不出来，
        /// 消息泵就永远出不来了：程序"卡死无响应"，主窗口停在最后画出的那一帧
        /// （用户截图停在"版本设置"页）。
        ///
        /// 现在：
        ///   ① 先无条件 <c>e.Handled = true</c>，绝不让它升级成进程级崩溃；
        ///   ② 处理过程**不可重入**（处理中再抛直接吞掉，不再递归弹窗）；
        ///   ③ 渲染线程失效是"死循环陷阱"：只报一次日志、不再弹任何窗口，
        ///      并尝试切到软件渲染让程序还能继续跑；
        ///   ④ 普通异常只发一条**非模态** toast（走 NotificationService，不启嵌套消息泵）；
        ///      异常处理路径里不再有任何 ShowDialog —— 那是"卡死无响应"的最后一条入口。
        /// </summary>
        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;   // 先标记已处理

            // ② 防重入：处理过程中再抛（日志/对话框自己出错）直接吞掉
            if (System.Threading.Interlocked.Exchange(ref _handlingDispatcherException, 1) == 1) return;
            try
            {
                int n = ++_dispatcherExceptionCount;

                bool renderThreadDead =
                    e.Exception is System.Runtime.InteropServices.COMException com &&
                    com.HResult == UCEERR_RENDERTHREADFAILURE;

                if (renderThreadDead)
                {
                    // ③ 渲染线程已经没了：任何模态窗口都画不出来，弹窗 = 卡死消息泵
                    if (!_renderThreadFailed)
                    {
                        _renderThreadFailed = true;
                        TsuruLauncher.Utilities.Logger.LogError(e.Exception,
                            "DispatcherUnhandledException(渲染线程失效，只报一次)");
                        TsuruLauncher.Utilities.Logger.LogInfo(
                            "[Crash] 渲染线程已失效（UCEERR_RENDERTHREADFAILURE）：已抑制后续弹窗" +
                            "（模态框画不出来且会卡死消息泵），并尝试切换软件渲染继续运行。" +
                            "若反复出现请检查显卡驱动 / 远程桌面虚拟显示器。");
                        TryFallbackToSoftwareRendering();
                    }
                    else
                    {
                        TsuruLauncher.Utilities.Logger.LogInfo(
                            $"[Crash] 渲染线程异常第 {n} 次，已抑制（不弹窗、不递归）");
                    }
                    return;
                }

                TsuruLauncher.Utilities.Logger.LogError(e.Exception, "DispatcherUnhandledException");

                // ④ 普通异常：只发一条**非模态**提示，绝不弹模态框、绝不再阻塞消息泵。
                //
                //    改前这里是 iOS26Dialog.Show(...) → Window.ShowDialog()：
                //    模态 + Topmost + **嵌套消息泵**。只要这个对话框自己没被画出来
                //    （渲染线程刚失效 / 窗口还没合成），ShowDialog 就会一直等一个永远不来的
                //    输入消息 —— Dispatcher 卡在嵌套泵里，主窗口停在最后合成的那一帧
                //    （用户截图：覆盖层停在半透明、整页发白、点什么都没反应）。
                //    异常处理路径本来就不该有阻塞式 UI，这里改成把提示丢给主窗口的
                //    NotificationService（纯数据 + toast，不启嵌套泵）。
                if (n <= 1)
                    NotifyNonBlocking("发生未处理的异常",
                        e.Exception.Message + "（详情见 TsuruLauncher.log）");
            }
            catch { }
            finally
            {
                System.Threading.Volatile.Write(ref _handlingDispatcherException, 0);
            }
        }

        /// <summary>
        /// 退出前把合并中的配置写盘落下去（<see cref="Services.ConfigService.SaveConfigDebounced"/>）。
        /// </summary>
        protected override void OnExit(ExitEventArgs e)
        {
            try { Services.ConfigService.Instance.FlushPendingSave(); } catch { }
            // 日志是异步写的：正常退出时把队列里剩下的行落盘再收线程。
            try { Utilities.Logger.Shutdown(); } catch { }
            base.OnExit(e);
        }

        /// <summary>
        /// 用主窗口的非模态通知（toast）提示一次异常。任何一步失败都只写日志 ——
        /// 这条路径的唯一硬要求是**绝不阻塞 Dispatcher**。
        /// </summary>
        private void NotifyNonBlocking(string title, string message)
        {
            try
            {
                var vm = (Current?.MainWindow as Window)?.DataContext as ViewModels.MainViewModel;
                if (vm != null) vm.NotificationService.ShowError(title, message);
                else TsuruLauncher.Utilities.Logger.LogInfo("[Crash] " + title + "：" + message);
            }
            catch (Exception ex)
            {
                try { TsuruLauncher.Utilities.Logger.LogError(ex, "NotifyNonBlocking"); } catch { }
            }
        }

        /// <summary>渲染线程失效后切软件渲染，尽量让程序还能继续响应。</summary>
        private static void TryFallbackToSoftwareRendering()
        {
            try
            {
                if (System.Windows.Media.RenderOptions.ProcessRenderMode != System.Windows.Interop.RenderMode.SoftwareOnly)
                    System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            }
            catch { }
        }

        private void TaskScheduler_UnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
             TsuruLauncher.Utilities.Logger.LogError(e.Exception, "TaskScheduler.UnobservedTaskException");
             e.SetObserved(); // Prevent crash
        }

        /// <summary>
        /// 进程级兜底。这里**不再弹模态框**：能走到这一步说明进程马上就要退出，
        /// 弹窗既救不回来，又可能在渲染线程已死时把消息泵卡住（"无响应"的直接观感）。
        /// 只写日志。
        /// </summary>
        private void CurrentDomain_UnhandledException(object sender, System.UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is System.Exception ex)
                TsuruLauncher.Utilities.Logger.LogError(ex, "CurrentDomain.UnhandledException");
            else
                TsuruLauncher.Utilities.Logger.LogError(
                    new Exception(e.ExceptionObject?.ToString() ?? "<null>"), "CurrentDomain.UnhandledException");
        }
        /// <summary>
        /// Raised after the language resource dictionary has been swapped.
        /// Pages subscribe (via MainWindow) to refresh their texts immediately.
        /// </summary>
        public static event Action? LanguageChanged;

        public static void SwitchLanguage(string languageCode)
        {
            var dict = new ResourceDictionary();
            
            switch (languageCode)
            {
                case "zh-CN":
                    dict.Source = new System.Uri("Resources/Languages/zh-CN.xaml", System.UriKind.Relative);
                    break;
                case "en-US":
                default:
                    dict.Source = new System.Uri("Resources/Languages/en-US.xaml", System.UriKind.Relative);
                    break;
            }
            
            // Remove old language dictionary
            var oldDict = Current.Resources.MergedDictionaries.FirstOrDefault(d => 
                d.Source != null && d.Source.OriginalString.Contains("Languages/"));
            
            if (oldDict != null)
            {
                Current.Resources.MergedDictionaries.Remove(oldDict);
            }
            
            // Add new language dictionary
            Current.Resources.MergedDictionaries.Add(dict);

            LanguageChanged?.Invoke();
        }
    }
}