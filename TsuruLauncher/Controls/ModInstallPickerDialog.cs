using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TsuruLauncher.Models;

namespace TsuruLauncher.Controls
{
    /// <summary>
    /// PCL2-style picker shown when installing a mod: choose which game
    /// version the mod should be installed into, or download it without
    /// installing. Handles version isolation automatically (each version
    /// shows its real mods folder).
    /// </summary>
    public static class ModInstallPicker
    {
        public class Result
        {
            public GameInstance? Target { get; set; }
            public bool DownloadOnly { get; set; }
            public bool Cancelled { get; set; }
            /// <summary>是否同时下载依赖项（从原「下载安装」卡片挪过来的选项）。</summary>
            public bool DownloadDependencies { get; set; }
        }

        public static Result Show(Window? owner, IList<GameInstance> versions, GameInstance? defaultVersion, string modName)
        {
            var darkBg = (Application.Current?.TryFindResource("PopoverBrush") as SolidColorBrush)
                         ?? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A221E")!);
            var greenAccent = Utilities.ThemeBrush.Accent;
            var dimText = Utilities.ThemeBrush.TextSecondary;
            var white = Utilities.ThemeBrush.TextPrimary;

            var dlg = new Window
            {
                Title = "安装 Mod",
                Width = 460,
                Height = 500,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true
            };

            var root = new Grid();
            var border = new Border
            {
                Background = darkBg,
                CornerRadius = new CornerRadius(20),
                ClipToBounds = true,
                Margin = new Thickness(12)
            };
            root.Children.Add(border);
            dlg.Content = root;

            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 18) };

            // Title
            panel.Children.Add(new TextBlock
            {
                Text = "🧩 安装 Mod",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = white
            });
            panel.Children.Add(new TextBlock
            {
                Text = $"选择要将 \"{modName}\" 安装到的游戏版本",
                FontSize = 12,
                Foreground = dimText,
                Margin = new Thickness(0, 4, 0, 14),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            // Version list
            var listBox = new ListBox
            {
                Background = Utilities.ThemeBrush.Surface4,
                BorderBrush = Utilities.ThemeBrush.Surface4,
                BorderThickness = new Thickness(1),
                MaxHeight = 260,
                Padding = new Thickness(6)
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(listBox, ScrollBarVisibility.Disabled);
            foreach (var v in versions)
            {
                bool isIsolated = !string.Equals(v.GameDir, v.RootPath, StringComparison.OrdinalIgnoreCase);
                string modsPath = Path.Combine(v.GameDir, "mods");
                var item = new ListBoxItem
                {
                    Tag = v,
                    Margin = new Thickness(0, 2, 0, 2),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Content = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = v.Id, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = white },
                            new TextBlock { Text = isIsolated ? $"📁 {modsPath}" : "📁 全局（版本隔离关闭）· " + modsPath, FontSize = 11, Foreground = dimText, TextTrimming = TextTrimming.CharacterEllipsis }
                        }
                    }
                };
                if (defaultVersion != null && v.Id == defaultVersion.Id) item.IsSelected = true;
                listBox.Items.Add(item);
            }
            listBox.SelectionChanged += (s, e) =>
            {
                if (listBox.SelectedItem is ListBoxItem li)
                    li.Background = Utilities.ThemeBrush.AccentAlpha(45);
            };
            panel.Children.Add(listBox);

            // Isolation note
            panel.Children.Add(new TextBlock
            {
                Text = "💡 版本隔离开启时，Mod 只影响对应版本；关闭时全局共享。",
                FontSize = 11,
                Foreground = dimText,
                Margin = new Thickness(0, 10, 0, 0),
                TextWrapping = TextWrapping.Wrap
            });

            // 「同时下载依赖项」—— 原本在资源详情页底部的「下载安装」卡片里。
            // 那张卡片已删（下载统一走版本行的 ⬇ 按钮），这个选项挪到这儿，功能不丢。
            var depsCheck = new CheckBox
            {
                Content = "同时下载依赖项",
                IsChecked = true,
                FontSize = 12.5,
                Foreground = Utilities.ThemeBrush.TextSecondary,
                Margin = new Thickness(0, 14, 0, 0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            panel.Children.Add(depsCheck);

            // Buttons
            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            panel.Children.Add(btnPanel);

            Button MakeButton(string text, Brush bg, Action onClick)
            {
                var b = new Button
                {
                    Content = text,
                    Padding = new Thickness(18, 8, 18, 8),
                    Margin = new Thickness(0, 0, 10, 0),
                    Foreground = white,
                    Background = bg,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    FontSize = 13
                };
                b.Click += (s, e) => onClick();
                return b;
            }

            Result? result = null;

            btnPanel.Children.Add(MakeButton("仅下载", Utilities.ThemeBrush.Surface4, () =>
            {
                result = new Result { DownloadOnly = true, DownloadDependencies = depsCheck.IsChecked == true };
                dlg.DialogResult = true;
            }));
            btnPanel.Children.Add(MakeButton("取消", Utilities.ThemeBrush.Surface4, () =>
            {
                result = new Result { Cancelled = true };
                dlg.DialogResult = true;
            }));
            btnPanel.Children.Add(MakeButton("安装到所选版本", greenAccent, () =>
            {
                var selected = listBox.SelectedItem is ListBoxItem li ? li.Tag as GameInstance : null;
                if (selected == null)
                {
                    iOS26Dialog.Show("请先选择一个游戏版本", "提示", DialogIcon.Warning, DialogButtons.OK);
                    return;
                }
                result = new Result { Target = selected, DownloadDependencies = depsCheck.IsChecked == true };
                dlg.DialogResult = true;
            }));

            // Drag to move
            dlg.MouseLeftButtonDown += (_, e) => { if (e.Source is Window || e.Source is Grid) dlg.DragMove(); };

            border.Child = panel;

            // Pop-in animation
            var transform = new ScaleTransform(0.92, 0.92);
            border.RenderTransform = transform;
            border.RenderTransformOrigin = new Point(0.5, 0.5);
            border.Opacity = 0;
            dlg.Loaded += (_, __) =>
            {
                var spring = new System.Windows.Media.Animation.BackEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut, Amplitude = 0.4 };
                transform.BeginAnimation(ScaleTransform.ScaleXProperty, new System.Windows.Media.Animation.DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(300)) { EasingFunction = spring });
                transform.BeginAnimation(ScaleTransform.ScaleYProperty, new System.Windows.Media.Animation.DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(300)) { EasingFunction = spring });
                border.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            };

            dlg.ShowDialog();
            return result ?? new Result { Cancelled = true };
        }
    }
}