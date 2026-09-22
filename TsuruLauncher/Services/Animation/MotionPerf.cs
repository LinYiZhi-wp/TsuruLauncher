using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Media;
using TsuruLauncher.Utilities;

namespace TsuruLauncher.Services.Animation
{
    /// <summary>
    /// 动效性能埋点（常开、开销极低）。
    ///
    /// 任务要求：「每次点击的同步阻塞 &lt; 3ms、动画期间不触发布局」。
    /// 这里把每个「点击 → 动画启动」的**同步阻塞耗时**写成一行
    /// <c>[MotionPerf] &lt;动作&gt; sync=X.XXms budget=3ms [OVER-BUDGET(+Yms)] &lt;细节&gt;</c>，
    /// 超预算会带 OVER-BUDGET 便于 grep。
    ///
    /// 与 <see cref="MotionDiagnostics"/> 的区别：这里**不做任何可视树遍历**，
    /// 只包一个 Stopwatch + 一行日志，所以可以安全地常开留在生产里；
    /// 而 <see cref="MotionDiagnostics"/> 的全树残留普查默认关闭（TSURU_MOTION_DBG=1 才开）。
    /// </summary>
    public static class MotionPerf
    {
        /// <summary>单次点击的同步阻塞预算（ms）。</summary>
        public const double BudgetMs = 3.0;

        private static readonly Stopwatch _clock = Stopwatch.StartNew();

        private static int _cacheAttachCount;
        private static long _cacheDevicePixels;
        private static int _cacheSkipCount;

        /// <summary>自进程启动以来的单调毫秒（用于校验日志时序）。</summary>
        public static double NowMs => _clock.Elapsed.TotalMilliseconds;

        /// <summary>用法：<c>using (MotionPerf.Measure("page-enter")) { ... }</c>，离开作用域时写日志。</summary>
        public static IDisposable Measure(string action, string? detail = null) => new Scope(action, detail);

        /// <summary>直接记一笔已知耗时。</summary>
        public static void Note(string action, double ms, string? detail = null)
        {
            try
            {
                string over = ms > BudgetMs ? $" OVER-BUDGET(+{ms - BudgetMs:0.00}ms)" : string.Empty;
                string tail = string.IsNullOrEmpty(detail) ? string.Empty : " " + detail;
                Logger.LogInfo($"[MotionPerf] {action} sync={ms:0.00}ms budget={BudgetMs:0}ms{over}{tail}");
            }
            catch { }
        }

        /// <summary>只记「跳过了动画」这类不需要耗时的事件。</summary>
        public static void NoteSkip(string action, string reason)
        {
            try { Logger.LogInfo($"[MotionPerf] {action} skipped sync=0.00ms budget={BudgetMs:0}ms reason={reason}"); }
            catch { }
        }

        /// <summary>
        /// 记录一次 BitmapCache 的挂/摘与**实际设备像素面积**。
        /// 任务要求「只动画期挂、结束即摘，不要永久缓存」，且不许把整页挂上缓存
        /// （全页缓存 = 单张 1.5x 位图 6~7MB，连续切页时渲染线程内存抖动就是
        ///  UCEERR_RENDERTHREADFAILURE 的直接来源）。
        /// </summary>
        public static void NoteCache(bool attached, string element, double devicePixels)
        {
            try
            {
                if (attached)
                {
                    _cacheAttachCount++;
                    _cacheDevicePixels += (long)devicePixels;
                }
                Logger.LogInfo(
                    $"[MotionPerf] cache-{(attached ? "on" : "off")} {element} " +
                    $"px={(long)devicePixels} attached={_cacheAttachCount} skipped={_cacheSkipCount} " +
                    $"totalMPx={_cacheDevicePixels / 1_000_000.0:0.00}");
            }
            catch { }
        }

        /// <summary>记录一次"因为太大/没尺寸所以不挂缓存"。</summary>
        public static void NoteCacheSkipped(string element, double devicePixels, double limit)
        {
            try
            {
                _cacheSkipCount++;
                Logger.LogInfo(
                    $"[MotionPerf] cache-skip {element} px={(long)devicePixels} limit={(long)limit} " +
                    $"skipped={_cacheSkipCount}");
            }
            catch { }
        }

        /// <summary>当前缓存统计（动作收尾时打一行汇总，便于改前/改后对比）。</summary>
        public static string CacheSummary()
            => $"cacheAttached={_cacheAttachCount} cacheSkipped={_cacheSkipCount} cacheMPx={_cacheDevicePixels / 1_000_000.0:0.00}";

        #region 逐帧间隔采样（TSURU_FRAME_DBG=1，默认关）

        /// <summary>
        /// 逐帧间隔采样开关。<c>TSURU_FRAME_DBG=1</c> 时启用。
        ///
        /// <para>为什么需要它：<see cref="Measure"/> 只量「点击回调同步阻塞」，
        /// 而用户感知到的「卡一下 / 不流畅」几乎都发生在**回调返回之后**的布局与渲染里 ——
        /// 同步埋点完全量不到。帧间隔是唯一的硬证据。</para>
        /// </summary>
        public static bool FrameProbeEnabled
        {
            get
            {
                try
                {
                    return string.Equals((Environment.GetEnvironmentVariable("TSURU_FRAME_DBG") ?? string.Empty).Trim(),
                        "1", StringComparison.Ordinal);
                }
                catch { return false; }
            }
        }

        /// <summary>
        /// 采样 <paramref name="windowMs"/> 内的帧间隔，收尾写一行
        /// <c>[MotionPerf] frames &lt;label&gt; n=.. active=.. idle=.. avg=.. p95=.. max=.. over16.7=..</c>。
        ///
        /// <para>两个必须注意的点：</para>
        /// <para>① 收口用 <see cref="System.Windows.Threading.DispatcherTimer"/> 按**墙钟**计，
        /// 不能按「累计帧时间」—— 动画跑完后 WPF 没有东西要合成，
        /// <see cref="CompositionTarget.Rendering"/> 就不再回调，按帧时间收口会永远等不到。</para>
        /// <para>② 间隔 &gt; <see cref="IdleGapMs"/> 的样本算「空闲」（那一帧本来就没东西要画，
        /// 不是掉帧），单独计数、不参与 avg/p95/max —— 否则动画结束后的那段空闲会把统计彻底带偏
        /// （实测能算出 2900ms 的假尖峰）。</para>
        /// <para>渲染线程一旦失效，Rendering 不再回调 → 这一行永远不出现（或 active 极少），
        /// 这本身就是「卡死」的判据。</para>
        /// </summary>
        public static void ProbeFrames(string label, double windowMs)
        {
            if (!FrameProbeEnabled) return;
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null) return;

                double start = NowMs;
                Logger.LogInfo($"[MotionPerf] frames {label} armed window={windowMs:0}ms t0={start:0}");

                double last = start;
                bool first = true;
                var gaps = new List<double>(256);

                EventHandler? handler = null;
                handler = (s, e) =>
                {
                    double now = NowMs;
                    if (!first) gaps.Add(now - last);
                    first = false;
                    last = now;
                };
                CompositionTarget.Rendering += handler;

                System.Windows.Threading.DispatcherTimer? timer = null;
                timer = new System.Windows.Threading.DispatcherTimer(
                    TimeSpan.FromMilliseconds(Math.Max(1.0, windowMs)),
                    System.Windows.Threading.DispatcherPriority.Background,
                    (_, __) =>
                    {
                        try { timer?.Stop(); } catch { }
                        try { CompositionTarget.Rendering -= handler; } catch { }
                        ReportFrames(label, gaps);
                    },
                    dispatcher);
                timer.Start();
            }
            catch { }
        }

        /// <summary>超过这个间隔的样本算「空闲」（没有东西要合成），不是掉帧。</summary>
        public const double IdleGapMs = 100.0;

        private static void ReportFrames(string label, List<double> gaps)
        {
            try
            {
                var active = new List<double>(gaps.Count);
                int idle = 0;
                foreach (double g in gaps)
                {
                    if (g > IdleGapMs) idle++;
                    else active.Add(g);
                }

                if (active.Count == 0)
                {
                    Logger.LogInfo($"[MotionPerf] frames {label} n={gaps.Count} active=0 idle={idle} " +
                                   $"(窗口内没有合成回调 —— 渲染线程可能已失效)");
                    return;
                }

                var sorted = new List<double>(active);
                sorted.Sort();

                double sum = 0.0;
                foreach (double g in active) sum += g;

                double avg = sum / active.Count;
                double p95 = sorted[(int)Math.Min(sorted.Count - 1, Math.Floor(sorted.Count * 0.95))];
                double max = sorted[sorted.Count - 1];

                // ── 按**实测刷新间隔**标定预算，而不是写死 16.7ms ────────────────────────
                // 16.7ms 只在 60Hz 显示器上成立。本机实测显示器是 180Hz（2560x1600），
                // 预算其实是 5.56ms —— 拿 16.7ms 当尺子会得出「一切正常」的错误结论。
                // 取间隔分布的 p10 作为刷新间隔的稳健估计（动画期间绝大多数帧都贴着刷新率）。
                double refreshMs = sorted[Math.Max(0, (int)(sorted.Count * 0.10))];
                if (refreshMs < 1.0) refreshMs = 1.0;
                double dropThreshold = refreshMs * 1.5;
                int dropped = 0;
                foreach (double g in active) if (g > dropThreshold) dropped++;

                Logger.LogInfo(
                    $"[MotionPerf] frames {label} n={gaps.Count} active={active.Count} idle={idle} " +
                    $"avg={avg:0.00}ms p95={p95:0.00}ms max={max:0.00}ms " +
                    $"refresh≈{refreshMs:0.00}ms(≈{1000.0 / refreshMs:0}Hz) dropped={dropped}" +
                    (dropped > 0 ? " DROPPED-FRAMES" : ""));

                // 逐帧明细（前 14 帧）：光有 avg/p95/max 只能知道「有几帧慢」，
                // 不知道**慢的是哪一帧** —— 而「第一帧慢」和「均匀慢」是两种完全不同的病，
                // 治法也完全不同。所以把开头这十几帧原样打出来。
                var head = new System.Text.StringBuilder();
                int headCount = Math.Min(14, gaps.Count);
                for (int i = 0; i < headCount; i++)
                {
                    if (i > 0) head.Append(' ');
                    head.Append((long)Math.Round(gaps[i]));
                }
                Logger.LogInfo($"[MotionPerf] frames {label} head(ms)=[{head}]");

                // 超预算帧的**位置**（第几帧 + 间隔 + 从第一帧起的累计毫秒）：
                // 位置比数值更有信息量 —— 落在开头是「冷启动/首帧」，落在中段是「某个定时器或归位动作」，
                // 落在末尾是「动画收尾的清理」。
                if (dropped > 0)
                {
                    var slow = new System.Text.StringBuilder();
                    double acc = 0.0;
                    int shown = 0;
                    for (int i = 0; i < gaps.Count && shown < 10; i++)
                    {
                        acc += gaps[i];
                        if (gaps[i] <= dropThreshold) continue;
                        if (shown > 0) slow.Append("; ");
                        slow.Append('#').Append(i + 1).Append('=')
                            .Append((long)Math.Round(gaps[i])).Append("ms@")
                            .Append((long)Math.Round(acc)).Append("ms");
                        shown++;
                    }
                    Logger.LogInfo($"[MotionPerf] frames {label} slow=[{slow}]");
                }
            }
            catch { }
        }

        /// <summary>帧间隔预算（60Hz）。</summary>
        public const double FrameBudgetMs = 16.7;

        /// <summary>
        /// 打一个带**进程内绝对时间**的标记（<c>[MotionPerf] mark &lt;label&gt; t=&lt;NowMs&gt;</c>）。
        /// 用途：把「frames ... slow=[#帧=ms@累计ms]」里的慢帧位置，和某个定时器/回调的触发时刻对上号
        /// —— 慢帧的 @NNNms 是相对 frames armed 那一行的 t0，两者相减即可。
        /// </summary>
        public static void Mark(string label)
        {
            if (!FrameProbeEnabled) return;
            try { Logger.LogInfo($"[MotionPerf] mark {label} t={NowMs:0}"); } catch { }
        }

        #endregion

        private sealed class Scope : IDisposable
        {
            private readonly string _action;
            private readonly string? _detail;
            private readonly long _startTicks;

            public Scope(string action, string? detail)
            {
                _action = action;
                _detail = detail;
                _startTicks = Stopwatch.GetTimestamp();
            }

            public void Dispose()
            {
                double ms = (Stopwatch.GetTimestamp() - _startTicks) * 1000.0 / Stopwatch.Frequency;
                Note(_action, ms, _detail);
            }
        }
    }
}
