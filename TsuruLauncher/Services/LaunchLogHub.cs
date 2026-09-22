using System;
using System.Collections.Generic;

namespace TsuruLauncher.Services
{
    public enum LaunchLogKind
    {
        Info,
        Out,
        Error,
        Command,
        Exit,
        Status
    }

    public class LaunchLogEntry
    {
        public LaunchLogKind Kind { get; set; } = LaunchLogKind.Info;
        public string Text { get; set; } = "";
        public DateTime Time { get; set; } = DateTime.Now;
    }

    /// <summary>
    /// 启动过程中所有输出（启动参数、stdout/stderr、退出码）的集中广播点，
    /// 供实时日志窗口订阅显示。即使窗口没打开也会保留缓冲区。
    /// </summary>
    public static class LaunchLogHub
    {
        private static readonly object Gate = new object();
        private static readonly List<LaunchLogEntry> Buffer = new List<LaunchLogEntry>();
        private const int MaxLines = 4000;

        public static event Action<LaunchLogEntry>? EntryAdded;
        public static event Action<string>? StatusChanged;

        public static bool IsGameRunning { get; private set; }
        public static string SessionTitle { get; private set; } = "未启动";
        public static string LastStatus { get; private set; } = "等待启动";
        public static double LastPercent { get; private set; }

        public static IReadOnlyList<LaunchLogEntry> Snapshot()
        {
            lock (Gate) return Buffer.ToArray();
        }

        public static void BeginSession(string title)
        {
            lock (Gate)
            {
                Buffer.Clear();
                SessionTitle = string.IsNullOrWhiteSpace(title) ? "Minecraft" : title;
                IsGameRunning = true;
            }
            LastStatus = "准备启动";
            LastPercent = 0;
            StatusChanged?.Invoke(LastStatus);
            Append(new LaunchLogEntry { Kind = LaunchLogKind.Status, Text = "===== 开始启动 " + SessionTitle + " =====" });
        }

        public static void EndSession(string summary)
        {
            IsGameRunning = false;
            LastStatus = summary;
            Append(new LaunchLogEntry { Kind = LaunchLogKind.Exit, Text = "===== " + summary + " =====" });
            StatusChanged?.Invoke(LastStatus);
        }

        public static void SetStatus(string status, double percent)
        {
            if (!string.IsNullOrWhiteSpace(status)) LastStatus = status;
            if (percent >= 0) LastPercent = percent;
            StatusChanged?.Invoke(LastStatus);
        }

        public static void Append(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return;
            Append(new LaunchLogEntry { Kind = Classify(raw), Text = raw });
        }

        public static void Append(LaunchLogKind kind, string text)
        {
            Append(new LaunchLogEntry { Kind = kind, Text = text });
        }

        private static void Append(LaunchLogEntry entry)
        {
            lock (Gate)
            {
                Buffer.Add(entry);
                if (Buffer.Count > MaxLines) Buffer.RemoveRange(0, Buffer.Count - MaxLines);
            }
            EntryAdded?.Invoke(entry);
        }

        private static LaunchLogKind Classify(string line)
        {
            if (line.StartsWith("[ERR]", StringComparison.Ordinal)) return LaunchLogKind.Error;
            if (line.StartsWith("[OUT]", StringComparison.Ordinal)) return LaunchLogKind.Out;
            if (line.StartsWith("[CMD]", StringComparison.Ordinal)) return LaunchLogKind.Command;
            if (line.StartsWith("[EXIT]", StringComparison.Ordinal)) return LaunchLogKind.Exit;
            return LaunchLogKind.Info;
        }
    }
}
