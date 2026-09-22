using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TsuruLauncher.Utilities;

namespace TsuruLauncher.Services.Animation
{
    /// <summary>
    /// 动效诊断探针 —— **默认关闭**，必须显式开 <c>TSURU_MOTION_DBG=1</c> 才工作。
    ///
    /// 为什么默认关：<see cref="Dump"/> 会对整棵可视树做残留普查
    /// （递归 + 每个节点 <c>TransformToAncestor</c>，visited 上限 6000 节点），
    /// 并且每一条结果都是一次同步 <c>File.AppendAllText</c>。
    /// 在设置页那种 963 节点、13 条静态命中的页面上，一次导航要额外写十几行日志、
    /// 走几万次可视树访问 —— 日常操作里这就是「每个板块都卡一下」的固定成本。
    /// 需要排查时再 <c>set TSURU_MOTION_DBG=1</c> 启动即可。
    ///
    /// 开启后：每次导航后在 DispatcherPriority.ApplicationIdle 打印
    ///   * 「点击 → 进入 Idle」的耗时（体感卡顿的直接量化）；
    ///   * 同步阻塞耗时（离场快照 RenderTargetBitmap 抓图那种一次性大开销）；
    ///   * 全部元素的残留 TranslateTransform.X/Y、Opacity、CacheMode、以及是否还有动画时钟；
    ///   * 页面根 / 内容容器在窗口坐标系里的实际位置（判定"整页偏移"）。
    /// 全部行以 [MotionDbg] 开头，便于 grep。
    /// </summary>
    public static class MotionDiagnostics
    {
        private static readonly bool _enabled =
            string.Equals(Environment.GetEnvironmentVariable("TSURU_MOTION_DBG"), "1", StringComparison.Ordinal);

        public static bool Enabled => _enabled;

        /// <summary>全树残留普查开关（默认关）。只有 TSURU_MOTION_DBG=1 时才做。</summary>
        public static bool DeepScanEnabled => _enabled;

        /// <summary>残留判定的容差：小于这个值就当 0。</summary>
        private const double Eps = 0.01;

        private static readonly Stopwatch _watch = new Stopwatch();
        private static int _nav;
        private static string _tag = "-";
        private static double _blockedMs;
        private static bool _pending;

        /// <summary>导航开始（用户点击 / 程序化跳转）时调用，开始计时。</summary>
        public static void NavigationStarted(string? tag)
        {
            if (!_enabled) return;
            try
            {
                _tag = tag ?? "-";
                _blockedMs = 0;
                _pending = true;
                _watch.Restart();
            }
            catch { }
        }

        /// <summary>记录一次同步阻塞（例如离场快照抓图）的耗时。</summary>
        public static void NoteBlocking(string what, double ms)
        {
            if (!_enabled) return;
            try
            {
                _blockedMs += ms;
                Logger.LogInfo($"[MotionDbg]   blocking {what}={ms:0.0}ms");
            }
            catch { }
        }

        public static double ElapsedMs => _watch.Elapsed.TotalMilliseconds;

        /// <summary>导航落地后调用：立刻报一次"进入 Idle"的耗时，随后在动画全部结束时做残留普查。</summary>
        public static void ScheduleVerify(Window window)
        {
            if (!_enabled || window == null) return;
            try
            {
                if (!_pending) NavigationStarted(_tag);   // 直接 GoBack / 语言重建等没走到 NavigationStarted
                _pending = false;
                int index = ++_nav;
                string tag = _tag;

                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
                {
                    try
                    {
                        Logger.LogInfo($"[MotionDbg] #{index} nav={tag} clickToIdle={_watch.Elapsed.TotalMilliseconds:0.0}ms " +
                                       $"blocking={_blockedMs:0.0}ms");
                    }
                    catch { }
                }));

                // 全部动效（最长 200ms 进场 + 168ms 错峰 + 180ms 单项）结束后再做残留普查
                var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(600), DispatcherPriority.Background,
                    (s, e) =>
                    {
                        ((DispatcherTimer)s!).Stop();
                        try { Dump(index, tag, window); } catch (Exception ex) { Logger.LogError(ex, "MotionDbg.Dump"); }
                    }, window.Dispatcher);
                timer.Start();
            }
            catch { }
        }

        /// <summary>残留普查：整棵可视树里只要还有位移 / 缓存 / 动画时钟就全部列出来。</summary>
        private static void Dump(int index, string tag, Window window)
        {
            var hits = new List<string>();
            var statics = new List<string>();
            int visited = 0;
            Scan(window, window, hits, statics, ref visited, 0);

            var sb = new StringBuilder();
            sb.Append($"[MotionDbg] #{index} nav={tag} settled={_watch.Elapsed.TotalMilliseconds:0.0}ms " +
                      $"nodes={visited} residual={hits.Count}");
            Logger.LogInfo(sb.ToString());

            foreach (var h in hits) Logger.LogInfo("[MotionDbg]   " + h);
            if (statics.Count > 0)
            {
                Logger.LogInfo($"[MotionDbg] #{index} static(non-motion)={statics.Count}");
                foreach (var s in statics) Logger.LogInfo("[MotionDbg]   (static) " + s);
            }

            Logger.LogInfo($"[MotionDbg] #{index} VERDICT " +
                           (hits.Count == 0 ? "clean (0 位移 / Opacity=1 / 无缓存 / 无残留时钟)" : $"dirty={hits.Count}"));

            // 关键元素的窗口坐标：用来判定"整页偏移"
            try
            {
                if (window.Content is DependencyObject)
                {
                    var page = FindPage(window);
                    if (page != null)
                    {
                        Logger.LogInfo($"[MotionDbg] #{index} page={page.GetType().Name} pos={Pos(page, window)} {Describe(page)}");
                        foreach (var name in new[] { "PageTransitionHost", "FrameContainer", "ResultsStack", "FeaturedPanel", "SearchResultsPanel" })
                        {
                            var el = FindByName(window, name);
                            if (el != null)
                                Logger.LogInfo($"[MotionDbg] #{index}   {name} pos={Pos(el, window)} {Describe(el)}");
                        }
                    }
                }
            }
            catch { }
        }

        private static void Scan(DependencyObject node, Window window, List<string> hits, List<string> statics, ref int visited, int depth)
        {
            if (depth > 60 || visited > 6000) return;
            visited++;

            if (node is FrameworkElement fe && node is not Window)
            {
                string why = DescribeResidual(fe, out bool counts);
                if (why.Length > 0)
                {
                    string path = PathOf(fe, window);
                    string line = $"{path} {why} pos={Pos(fe, window)}";
                    if (counts) { if (hits.Count <= 40) hits.Add(line); }
                    else if (statics.Count <= 12) statics.Add(line);
                }
            }

            int count = 0;
            try { count = VisualTreeHelper.GetChildrenCount(node); } catch { return; }
            for (int i = 0; i < count; i++)
            {
                DependencyObject child;
                try { child = VisualTreeHelper.GetChild(node, i); } catch { continue; }
                Scan(child, window, hits, statics, ref visited, depth + 1);
            }
        }

        /// <summary>
        /// 返回该元素的异常描述；<paramref name="counts"/> = 是否算作真正的"动效残留"。
        /// 只有"带活动时钟的非静止值"或"还挂着 BitmapCache"才算残留；
        /// XAML 里本来就写死的位移（例如开关旋钮 Tx=±9）只是静态设计值，单独标记、不计入判定。
        /// </summary>
        private static string DescribeResidual(FrameworkElement fe, out bool counts)
        {
            counts = false;
            var parts = new List<string>();
            try
            {
                if (fe.Visibility != Visibility.Visible) return string.Empty;   // 不渲染的不算
                if (fe.ActualWidth < 0.5 || fe.ActualHeight < 0.5) return string.Empty;

                bool clock = HasLiveClock(fe);
                bool designState = IsDesignStateElement(fe);
                var (x, y, sx, sy) = TransformOf(fe);
                if (designState) clock = false;   // 模板里的开关状态位移，不算动效残留
                if (Math.Abs(x) > Eps) { parts.Add($"Tx={x:0.##}"); if (clock) counts = true; }
                if (Math.Abs(y) > Eps) { parts.Add($"Ty={y:0.##}"); if (clock) counts = true; }
                if (Math.Abs(sx - 1.0) > Eps) { parts.Add($"Sx={sx:0.###}"); if (clock) counts = true; }
                if (Math.Abs(sy - 1.0) > Eps) { parts.Add($"Sy={sy:0.###}"); if (clock) counts = true; }

                // 半透明：只有"动画停在中间"才算残留（0.3 / 0.5 / 0.6 这类静态不透明度是正常静止态）
                if (fe.Opacity > 0.001 && fe.Opacity < 0.999 && fe.HasAnimatedProperties)
                {
                    parts.Add($"Op={fe.Opacity:0.###}(anim)");
                    counts = true;
                }

                if (fe.CacheMode is BitmapCache) { parts.Add("Cache=BitmapCache"); counts = true; }
                if (clock) parts.Add("clock=live");
            }
            catch { }
            return string.Join(" ", parts);
        }

        /// <summary>
        /// 这些是 ControlTemplate 里由 XAML 触发器 Storyboard 驱动的"状态位移"
        /// （开关旋钮 on = translateX(9px)、滑块等），HoldEnd 会让时钟一直挂着，
        /// 但它们不是页面动效的残留，不该计入判定。
        /// </summary>
        private static bool IsDesignStateElement(FrameworkElement fe)
        {
            switch (fe.Name)
            {
                case "ToggleEllipse":
                case "ActiveToggleEllipse":
                case "ToggleRectangle":
                case "ActiveToggleRectangle":
                    return true;
                default:
                    return false;
            }
        }

        private static bool HasLiveClock(FrameworkElement fe)
        {
            try
            {
                if (fe.HasAnimatedProperties) return true;
                var t = fe.RenderTransform;
                return TransformHasClock(t);
            }
            catch { return false; }
        }

        private static bool TransformHasClock(Transform? t)
        {
            switch (t)
            {
                case null: return false;
                case TransformGroup g:
                    foreach (var c in g.Children) if (TransformHasClock(c)) return true;
                    return false;
                default:
                    try { return t.HasAnimatedProperties; } catch { return false; }
            }
        }

        private static (double X, double Y, double Sx, double Sy) TransformOf(FrameworkElement fe)
        {
            double x = 0, y = 0, sx = 1, sy = 1;
            void Walk(Transform? t)
            {
                switch (t)
                {
                    case null: return;
                    case TransformGroup g:
                        foreach (var c in g.Children) Walk(c);
                        return;
                    case TranslateTransform tr:
                        x += tr.X; y += tr.Y;
                        return;
                    case ScaleTransform sc:
                        sx *= sc.ScaleX; sy *= sc.ScaleY;
                        return;
                }
            }
            try { Walk(fe.RenderTransform); } catch { }
            return (x, y, sx, sy);
        }

        private static string Describe(FrameworkElement fe)
        {
            var (x, y, sx, sy) = TransformOf(fe);
            string cache = fe.CacheMode is BitmapCache ? "Cache=BitmapCache" : "Cache=none";
            return $"T=({x:0.##},{y:0.##}) S=({sx:0.###},{sy:0.###}) Op={fe.Opacity:0.###} {cache} " +
                   $"clock={(HasLiveClock(fe) ? "live" : "none")} size={fe.ActualWidth:0.#}x{fe.ActualHeight:0.#}";
        }

        private static string Pos(FrameworkElement fe, Window window)
        {
            try
            {
                var p = fe.TransformToAncestor(window).Transform(new Point(0, 0));
                return $"({p.X:0.##},{p.Y:0.##})";
            }
            catch { return "(?)"; }
        }

        private static string PathOf(FrameworkElement fe, Window window)
        {
            var sb = new StringBuilder();
            DependencyObject? cur = fe;
            int guard = 0;
            while (cur != null && cur is not Window && guard++ < 40)
            {
                string seg = cur is FrameworkElement f && !string.IsNullOrEmpty(f.Name)
                    ? $"{f.GetType().Name}#{f.Name}"
                    : cur.GetType().Name;
                sb.Insert(0, "/" + seg);
                try { cur = VisualTreeHelper.GetParent(cur); } catch { break; }
            }
            return sb.ToString();
        }

        private static FrameworkElement? FindPage(Window window)
        {
            var frame = FindByName(window, "RootFrame") as System.Windows.Controls.Frame;
            return frame?.Content as FrameworkElement;
        }

        private static FrameworkElement? FindByName(DependencyObject root, string name)
        {
            int count = 0;
            try { count = VisualTreeHelper.GetChildrenCount(root); } catch { return null; }
            for (int i = 0; i < count; i++)
            {
                DependencyObject child;
                try { child = VisualTreeHelper.GetChild(root, i); } catch { continue; }
                if (child is FrameworkElement fe && fe.Name == name) return fe;
                var found = FindByName(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}
