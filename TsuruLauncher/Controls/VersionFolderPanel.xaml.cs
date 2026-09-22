using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TsuruLauncher.Models;
using TsuruLauncher.Services;
using TsuruLauncher.Services.Animation;

namespace TsuruLauncher.Controls
{
    /// <summary>
    /// 「版本选择」页的**文件夹列表**，承载在右侧信息栏里。
    ///
    /// <para>为什么从左边搬到右边：原来它是版本列表左侧的一列（还要靠标题栏那个 📂 按钮切换显隐），
    /// 一展开就把版本列表挤窄，观感很别扭；而且右栏在那一页本来就是空的（只显示首页那几张卡），
    /// 信息密度极低。现在整块搬过来，📂 按钮删掉 —— 没有可切换的东西了。</para>
    ///
    /// <para>选中某个文件夹后通过 <see cref="FolderSelected"/> 静态事件通知页面重新加载版本
    /// （控件与页面没有直接引用关系，避免把右栏和页面耦死）。</para>
    /// </summary>
    public partial class VersionFolderPanel : UserControl
    {
        /// <summary>选中了某个游戏目录（参数是目录路径）。</summary>
        public static event Action<string>? FolderSelected;

        private readonly ObservableCollection<GameDirectory> _directories = new();
        private GameDirectory? _active;

        public VersionFolderPanel()
        {
            InitializeComponent();
            FolderList.ItemsSource = _directories;
            Loaded += (_, __) => Reload();
        }

        /// <summary>重新探测游戏目录并刷新列表（页面每次进入都会调）。</summary>
        public void Reload()
        {
            try
            {
                var detected = new VersionDetectionService().DetectGameDirectories();
                _directories.Clear();
                foreach (var d in detected) _directories.Add(d);

                CountText.Text = _directories.Count == 0
                    ? "没有检测到游戏目录"
                    : $"共 {_directories.Count} 个目录";

                Utilities.Logger.LogInfo("[FolderPanel] reload dirs=" + _directories.Count +
                    " names=" + string.Join("/", _directories.Select(d => d.Name)));
                if (_directories.Count > 0) Select(_directories[0]);
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "VersionFolderPanel.Reload");
            }
        }

        private void Select(GameDirectory dir)
        {
            _active = dir;
            foreach (var child in EnumerateRows())
            {
                bool on = ReferenceEquals(child.Tag, dir);
                child.Background = on ? Res("AccentAlphaBrush") ?? child.Background : Res("Surface2Brush") ?? child.Background;
                child.BorderBrush = on ? Res("AccentBrush") ?? child.BorderBrush : Res("GlassBorderBrush") ?? child.BorderBrush;
                child.BorderThickness = new Thickness(on ? 1.5 : 1);
            }
            FolderSelected?.Invoke(dir.Path);
        }

        private IEnumerable<Border> EnumerateRows()
        {
            var stack = new Stack<DependencyObject>();
            stack.Push(FolderList);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                int n = VisualTreeHelper.GetChildrenCount(cur);
                for (int i = 0; i < n; i++)
                {
                    var c = VisualTreeHelper.GetChild(cur, i);
                    if (c is Border b && b.Tag is GameDirectory) yield return b;
                    stack.Push(c);
                }
            }
        }

        private static Brush? Res(string key)
        {
            try { return Application.Current?.TryFindResource(key) as Brush; }
            catch { return null; }
        }

        /// <summary>列表项进场：220ms 淡入 + 上浮 8px，按索引错峰 30ms（Axolotl 那套 stagger）。</summary>
        private void Row_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Border row) return;
            int index = _directories.IndexOf(row.Tag as GameDirectory ?? new GameDirectory());
            var delay = TimeSpan.FromMilliseconds(Math.Max(0, index) * 30);
            PageTransition.PlayOpacity(row, 1.0, 220, AxolotlMotion.EaseOut, delay.TotalMilliseconds);
        }

        private void Row_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border row && row.Tag is GameDirectory dir) Select(dir);
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择 .minecraft 文件夹中的任意文件",
                Filter = "All files (*.*)|*.*"
            };
            if (dialog.ShowDialog() != true) return;

            string? path = System.IO.Path.GetDirectoryName(dialog.FileName);
            while (!string.IsNullOrEmpty(path) &&
                   !System.IO.Directory.Exists(System.IO.Path.Combine(path, "versions")))
            {
                path = System.IO.Path.GetDirectoryName(path);
            }

            if (string.IsNullOrEmpty(path) ||
                !System.IO.Directory.Exists(System.IO.Path.Combine(path, "versions")))
            {
                iOS26Dialog.Show("未找到有效的游戏目录\n请选择 .minecraft 文件夹", "错误", DialogIcon.Error, DialogButtons.OK);
                return;
            }

            var added = new GameDirectory
            {
                Name = System.IO.Path.GetFileName(path),
                Path = path,
                IsDefault = false,
                Source = DirectorySource.Manual
            };
            _directories.Add(added);
            CountText.Text = $"共 {_directories.Count} 个目录";
            Select(added);
        }
    }
}
