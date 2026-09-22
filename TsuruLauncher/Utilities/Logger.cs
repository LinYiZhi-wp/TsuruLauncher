using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading;

namespace TsuruLauncher.Utilities
{
    /// <summary>
    /// 文件日志。
    ///
    /// <para><b>为什么是异步的</b>：原实现每次 <c>Log()</c> 都在**调用线程**（也就是 UI 线程）上
    /// <c>File.AppendAllText</c> —— 打开句柄 + 定位到末尾 + 写入 + 关闭，一次 0.3~2ms，
    /// 遇到杀软扫描/磁盘压力还会飙到 10~30ms。而一次「打开实验室工具」就要写 7~8 行日志，
    /// 落点正好散在点击帧与 240/260/320ms 那几个定时器上 —— 实测的慢帧位置与之完全吻合。
    /// 现在 <c>Log()</c> 只做入队，写盘交给一条后台线程（保持句柄常开 + AutoFlush）。</para>
    ///
    /// <para><b>可见性</b>：<c>AutoFlush = true</c> 让每行写完就 Flush 到操作系统，
    /// 所以即使进程被强杀（<c>taskkill /F</c>），已经入队并落盘的行依然能被外部读取 ——
    /// 自动化脚本读日志不受影响。最坏情况只丢最后 ~250ms 内还没被后台线程取走的行。</para>
    /// </summary>
    public static class Logger
    {
        private static readonly string LogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TsuruLauncher.log");
        private static readonly object _lock = new object();
        private const long MaxLogSizeBytes = 5 * 1024 * 1024;

        /// <summary>待写入的行（含行尾换行符）。</summary>
        private static readonly ConcurrentQueue<string> _queue = new ConcurrentQueue<string>();

        /// <summary>唤醒后台写线程。</summary>
        private static readonly AutoResetEvent _signal = new AutoResetEvent(false);

        private static Thread? _worker;
        private static volatile bool _stop;

        public static void Initialize()
        {
            try
            {
                lock (_lock)
                {
                    if (File.Exists(LogPath))
                    {
                        var info = new FileInfo(LogPath);
                        if (info.Length > MaxLogSizeBytes)
                        {
                            string archivePath = LogPath.Replace(".log", $".{DateTime.Now:yyyyMMdd_HHmmss}.log");
                            File.Move(LogPath, archivePath);

                            int maxArchives = 3;
                            var archives = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "TsuruLauncher.*.log")
                                .OrderByDescending(f => f).Skip(maxArchives).ToList();
                            foreach (var old in archives)
                            {
                                try { File.Delete(old); } catch { }
                            }
                        }
                    }
                }
            }
            catch { }

            EnsureWorker();
        }

        /// <summary>启动后台写线程（幂等）。</summary>
        private static void EnsureWorker()
        {
            if (_worker != null) return;
            lock (_lock)
            {
                if (_worker != null) return;
                _stop = false;
                _worker = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "TsuruLogWriter",
                    Priority = ThreadPriority.BelowNormal
                };
                _worker.Start();
            }
        }

        private static void WorkerLoop()
        {
            StreamWriter? writer = null;

            StreamWriter? TryOpen()
            {
                try
                {
                    var fs = new FileStream(LogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    return new StreamWriter(fs) { AutoFlush = true };
                }
                catch { return null; }
            }

            writer = TryOpen();

            while (!_stop)
            {
                try { _signal.WaitOne(250); } catch { }
                if (writer == null) writer = TryOpen();
                Drain(writer);
            }

            Drain(writer);
            try { writer?.Flush(); writer?.Dispose(); } catch { }
        }

        private static void Drain(StreamWriter? writer)
        {
            while (_queue.TryDequeue(out var line))
            {
                try { writer?.Write(line); } catch { /* 尽力而为 */ }
            }
        }

        public static void Log(string message, string type = "INFO")
        {
            try
            {
                string logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{type}] {message}{Environment.NewLine}";
                EnsureWorker();
                _queue.Enqueue(logLine);
                _signal.Set();
            }
            catch { /* Best effort logging */ }
        }

        /// <summary>把队列里剩下的行写出去（退出前调用；平时不需要）。</summary>
        public static void Flush()
        {
            try { _signal.Set(); } catch { }
        }

        /// <summary>停止后台写线程并做最后一次落盘。</summary>
        public static void Shutdown()
        {
            try
            {
                _stop = true;
                try { _signal.Set(); } catch { }
                try { _worker?.Join(TimeSpan.FromMilliseconds(800)); } catch { }
                _worker = null;
            }
            catch { }
        }

        public static void LogError(Exception ex, string context)
        {
            Log($"[{context}] {ex.Message}\nStackTrace: {ex.StackTrace}", "ERROR");
            if (ex.InnerException != null)
            {
                Log($"InnerException: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}", "ERROR-INNER");
            }
        }

        public static void LogInfo(string message)
        {
            Log(message, "INFO");
        }

        public static void LogWarning(string message)
        {
            Log(message, "WARNING");
        }

        public static void LogDebug(string message)
        {
            Log(message, "DEBUG");
        }

        public static void LogCritical(string message)
        {
            Log(message, "CRITICAL");
        }

        public static void LogGameOutput(string message)
        {
            Log(message, "GAME-OUTPUT");
        }

        public static void LogGameError(string message)
        {
            Log(message, "GAME-ERROR");
        }

        public static void ClearLog()
        {
            try
            {
                lock (_lock)
                {
                    if (File.Exists(LogPath))
                    {
                        File.WriteAllText(LogPath, $"--- Log Cleared at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ---\n");
                    }
                }
            }
            catch { /* Best effort */ }
        }

        public static string GetLogPath()
        {
            return LogPath;
        }
    }
}
