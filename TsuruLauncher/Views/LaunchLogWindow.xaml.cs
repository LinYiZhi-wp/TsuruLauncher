using System;
using TsuruLauncher.Controls;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using TsuruLauncher.Services;
using TsuruLauncher.Utilities;

namespace TsuruLauncher.Views
{
    /// <summary>
    /// PCL2 风格的实时启动日志窗口：显示启动参数、游戏 stdout / stderr 与退出码。
    /// </summary>
    public partial class LaunchLogWindow : Window
    {
        private static LaunchLogWindow? _instance;

        private readonly Paragraph _paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 18 };
        private readonly List<LaunchLogEntry> _lines = new List<LaunchLogEntry>();
        private const int MaxLines = 3000;
        private bool _wrapped = true;
        private bool _subscribed;

        public LaunchLogWindow()
        {
            InitializeComponent();

            var doc = new FlowDocument(_paragraph)
            {
                PagePadding = new Thickness(0),
                FontFamily = new FontFamily("Cascadia Mono,Consolas,Microsoft YaHei UI"),
                LineHeight = 18
            };
            LogBox.Document = doc;

            Loaded += OnLoaded;
            Closed += OnClosed;
            UpdateHeader();
        }

        public static void ShowWindow(string versionId)
        {
            try
            {
                if (_instance == null)
                {
                    _instance = new LaunchLogWindow();
                    if (Application.Current != null && Application.Current.MainWindow != null && Application.Current.MainWindow != _instance)
                        _instance.Owner = Application.Current.MainWindow;
                }

                _instance.Title = "启动日志 · " + (string.IsNullOrWhiteSpace(versionId) ? "Tsuru Launcher" : versionId);
                if (!_instance.IsVisible) _instance.Show();
                if (_instance.WindowState == WindowState.Minimized) _instance.WindowState = WindowState.Normal;
                _instance.Activate();
                _instance.ScrollToEnd();
            }
            catch (Exception ex)
            {
                Logger.LogDebug("[LogWindow] 打开失败: " + ex.Message);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (!_subscribed)
            {
                LaunchLogHub.EntryAdded += OnEntryAdded;
                LaunchLogHub.StatusChanged += OnStatusChanged;
                _subscribed = true;
            }

            LogBox.Document.Blocks.Clear();
            _paragraph.Inlines.Clear();
            _lines.Clear();
            foreach (var entry in LaunchLogHub.Snapshot()) AddLine(entry, false);
            UpdateHeader();
            ScrollToEnd();
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            if (_subscribed)
            {
                LaunchLogHub.EntryAdded -= OnEntryAdded;
                LaunchLogHub.StatusChanged -= OnStatusChanged;
                _subscribed = false;
            }
            if (ReferenceEquals(_instance, this)) _instance = null;
        }

        private void OnEntryAdded(LaunchLogEntry entry)
        {
            var dispatcher = Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) AddLine(entry, true);
            else dispatcher.BeginInvoke(new Action(() => AddLine(entry, true)), DispatcherPriority.Background);
        }

        private void OnStatusChanged(string status)
        {
            var dispatcher = Dispatcher;
            if (dispatcher == null || dispatcher.CheckAccess()) UpdateHeader();
            else dispatcher.BeginInvoke(new Action(UpdateHeader), DispatcherPriority.Background);
        }

        private void AddLine(LaunchLogEntry entry, bool autoScroll)
        {
            var run = new Run(entry.Time.ToString("HH:mm:ss") + "  " + entry.Text)
            {
                Foreground = BrushFor(entry.Kind)
            };
            _paragraph.Inlines.Add(run);
            _paragraph.Inlines.Add(new LineBreak());
            _lines.Add(entry);

            while (_lines.Count > MaxLines)
            {
                _lines.RemoveAt(0);
                if (_paragraph.Inlines.Count >= 2)
                {
                    _paragraph.Inlines.Remove(_paragraph.Inlines.FirstInline);
                    if (_paragraph.Inlines.FirstInline != null) _paragraph.Inlines.Remove(_paragraph.Inlines.FirstInline);
                }
            }

            if (autoScroll && AutoScrollCheck.IsChecked == true) ScrollToEnd();
        }

        private void UpdateHeader()
        {
            TitleText.Text = "启动日志 · " + LaunchLogHub.SessionTitle;
            SubTitleText.Text = LaunchLogHub.IsGameRunning ? "游戏运行中" : "游戏未运行（可保留日志查看）";
            StatusText.Text = LaunchLogHub.LastStatus;
            StateText.Text = LaunchLogHub.IsGameRunning ? "运行中" : "已结束";
            StatusText.Foreground = LaunchLogHub.IsGameRunning ? new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80)) : new SolidColorBrush(Color.FromRgb(0x93, 0xA3, 0xB8));
            StateText.Foreground = StatusText.Foreground;
            StatusProgress.Value = Math.Max(0, Math.Min(100, LaunchLogHub.LastPercent));
        }

        private static Brush BrushFor(LaunchLogKind kind)
        {
            switch (kind)
            {
                case LaunchLogKind.Error: return new SolidColorBrush(Color.FromRgb(0xFF, 0x7B, 0x7B));
                case LaunchLogKind.Out: return new SolidColorBrush(Color.FromRgb(0x8F, 0xA3, 0xBB));
                case LaunchLogKind.Command: return new SolidColorBrush(Color.FromRgb(0x7C, 0xC7, 0xFF));
                case LaunchLogKind.Exit: return new SolidColorBrush(Color.FromRgb(0xFA, 0xCC, 0x15));
                case LaunchLogKind.Status: return new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                default: return new SolidColorBrush(Color.FromRgb(0xC6, 0xD2, 0xE2));
            }
        }

        private void ScrollToEnd()
        {
            try { LogBox.ScrollToEnd(); } catch { }
        }

        private string AllText()
        {
            var sb = new StringBuilder();
            foreach (var line in _lines) sb.Append(line.Time.ToString("HH:mm:ss")).Append("  ").Append(line.Text).Append(Environment.NewLine);
            return sb.ToString();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(AllText());
                SetFooterHint("已复制 " + _lines.Count + " 行到剪贴板");
            }
            catch (Exception ex) { iOS26Dialog.Show("复制失败: " + ex.Message, "错误", DialogIcon.Error, DialogButtons.OK); }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "日志文件 (*.log)|*.log|文本文件 (*.txt)|*.txt",
                FileName = "launch-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log",
                Title = "保存启动日志"
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                File.WriteAllText(dialog.FileName, AllText(), new UTF8Encoding(false));
                SetFooterHint("已保存到 " + dialog.FileName);
            }
            catch (Exception ex) { iOS26Dialog.Show("保存失败: " + ex.Message, "错误", DialogIcon.Error, DialogButtons.OK); }
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _paragraph.Inlines.Clear();
            _lines.Clear();
            SetFooterHint("已清空显示（缓冲区仍保留最近日志）");
        }

        private void Wrap_Click(object sender, RoutedEventArgs e)
        {
            _wrapped = !_wrapped;
            LogBox.Document.PageWidth = _wrapped ? double.NaN : 4000;
            LogBox.HorizontalScrollBarVisibility = _wrapped ? System.Windows.Controls.ScrollBarVisibility.Disabled : System.Windows.Controls.ScrollBarVisibility.Auto;
            SetFooterHint(_wrapped ? "已开启自动换行" : "已关闭自动换行（可横向滚动）");
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TsuruLauncher");
                Directory.CreateDirectory(dir);
                System.Diagnostics.Process.Start("explorer.exe", dir);
            }
            catch (Exception ex) { iOS26Dialog.Show("打开失败: " + ex.Message, "错误", DialogIcon.Error, DialogButtons.OK); }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void SetFooterHint(string text)
        {
            SubTitleText.Text = text;
        }
    }
}
