using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace GeminiLauncher.Controls
{
    public static class iOS26Dialog
    {
        public static bool? Show(Window? owner, string message, string title = "提示", DialogIcon icon = DialogIcon.Info, DialogButtons buttons = DialogButtons.OK)
        {
            var cardBg = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1C1C24")!);
            var greenAccent = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E676")!);
            var redAccent = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5252")!);
            var yellowAccent = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFB300")!);
            var blueAccent = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#42A5F5")!);
            var white = Brushes.White;
            var dimText = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B0FFFFFF")!);

            bool? result = null;

            var dlg = new Window
            {
                Title = "",
                Width = 400,
                Height = buttons == DialogButtons.OK ? 240 : 276,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true
            };

            var rootGrid = new Grid();
            dlg.Content = rootGrid;

            var cardTransform = new ScaleTransform(0.88, 0.88);
            var translateTransform = new TranslateTransform(0, 12);

            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(cardTransform);
            transformGroup.Children.Add(translateTransform);

            var mainBorder = new Border
            {
                Background = cardBg,
                CornerRadius = new CornerRadius(20),
                ClipToBounds = true,
                RenderTransform = transformGroup,
                RenderTransformOrigin = new Point(0.5, 0.5),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            rootGrid.Children.Add(mainBorder);

            var outerPanel = new StackPanel();

            var titleBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Height = 52,
                VerticalAlignment = VerticalAlignment.Center
            };

            string iconGlyph;
            SolidColorBrush iconColor;
            switch (icon)
            {
                case DialogIcon.Error:
                    iconGlyph = "✕"; iconColor = redAccent; break;
                case DialogIcon.Warning:
                    iconGlyph = "⚠"; iconColor = yellowAccent; break;
                case DialogIcon.Success:
                    iconGlyph = "✓"; iconColor = greenAccent; break;
                default:
                    iconGlyph = "ℹ"; iconColor = blueAccent; break;
            }

            var iconCircle = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(30, iconColor.Color.R, iconColor.Color.G, iconColor.Color.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, iconColor.Color.R, iconColor.Color.G, iconColor.Color.B)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(20, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            iconCircle.Child = new TextBlock
            {
                Text = iconGlyph,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = iconColor,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = white,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 0)
            };

            titleBar.Children.Add(iconCircle);
            titleBar.Children.Add(titleText);

            var closeBtnBg = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            var closeBtn = new Button
            {
                Content = "✕",
                Width = 32,
                Height = 32,
                FontSize = 11,
                Foreground = dimText,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            closeBtnBg.Child = closeBtn;
            closeBtn.MouseEnter += (_, __) => closeBtnBg.Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
            closeBtn.MouseLeave += (_, __) => closeBtnBg.Background = Brushes.Transparent;
            closeBtn.Click += (_, __) =>
            {
                AnimateClose(dlg, mainBorder, cardTransform, translateTransform);
                dlg.DialogResult = false;
            };

            var titleWrapper = new Grid();
            titleWrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleWrapper.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(titleBar, 0);
            Grid.SetColumn(closeBtnBg, 1);
            titleWrapper.Children.Add(titleBar);
            titleWrapper.Children.Add(closeBtnBg);

            outerPanel.Children.Add(titleWrapper);
            outerPanel.Children.Add(new Rectangle { Height = 1, Fill = new SolidColorBrush(Color.FromArgb(22, 255, 255, 255)) });

            outerPanel.Children.Add(new TextBlock
            {
                Text = message,
                FontSize = 14,
                Foreground = dimText,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(24, 18, 24, 4),
                MaxHeight = 90
            });

            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(20, 10, 20, 18)
            };

            Action<bool?> closeWithResult = (r) =>
            {
                result = r;
                AnimateClose(dlg, mainBorder, cardTransform, translateTransform);
                dlg.DialogResult = r;
            };

            if (buttons == DialogButtons.OKCancel || buttons == DialogButtons.YesNoCancel)
            {
                btnPanel.Children.Add(CreateStyledButton("取消", dimText, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A38")!), () => closeWithResult(false)));
                btnPanel.Children.Add(new FrameworkElement() { Width = 10 });
            }

            if (buttons == DialogButtons.YesNo || buttons == DialogButtons.YesNoCancel)
                btnPanel.Children.Add(CreateStyledButton("确定", white, greenAccent, () => closeWithResult(true)));
            else if (buttons == DialogButtons.OK || buttons == DialogButtons.OKCancel)
                btnPanel.Children.Add(CreateStyledButton("确定", white, greenAccent, () => closeWithResult(true)));

            if (buttons == DialogButtons.YesNo && !HasButton(btnPanel, "取消"))
            {
                btnPanel.Children.Insert(0, CreateStyledButton("取消", dimText, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A2A38")!), () => closeWithResult(false)));
                btnPanel.Children.Insert(1, new FrameworkElement() { Width = 10 });
            }

            outerPanel.Children.Add(btnPanel);
            mainBorder.Child = outerPanel;

            dlg.MouseLeftButtonDown += (_, e) => { if (e.Source is Window || e.Source is Grid) dlg.DragMove(); };

            dlg.Loaded += (_, __) =>
            {
                // PCL2-style pop: springy scale with a tiny overshoot, then settle
                var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };
                var scaleAnim = new DoubleAnimation(0.88, 1.0, TimeSpan.FromMilliseconds(340)) { EasingFunction = spring };
                cardTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                cardTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
                translateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = spring });
            };

            dlg.ShowDialog();
            return result;
        }

        public static bool? Show(string message, string title = "提示", DialogIcon icon = DialogIcon.Info, DialogButtons buttons = DialogButtons.OK)
        {
            return Show(Application.Current?.MainWindow, message, title, icon, buttons);
        }

        private static void AnimateClose(Window dlg, Border card, ScaleTransform scale, TranslateTransform trans)
        {
            var sd = new DoubleAnimation(1.0, 0.94, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, sd);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, sd);
            trans.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -8, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });
        }

        private static bool HasButton(StackPanel panel, string text)
        {
            foreach (var child in panel.Children)
                if (child is Button b && b.Content?.ToString() == text) return true;
            return false;
        }

        private static Border CreateStyledButton(string text, SolidColorBrush fg, SolidColorBrush bg, Action onClick)
        {
            var btnBorder = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = bg,
                Padding = new Thickness(22, 9, 22, 9),
                Cursor = Cursors.Hand
            };
            btnBorder.Child = new TextBlock { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = fg };

            var hoverBg = new SolidColorBrush(Color.FromArgb((byte)Math.Min(255, bg.Color.A + 45), (byte)Math.Min(255, bg.Color.R + 15), (byte)Math.Min(255, bg.Color.G + 15), (byte)Math.Min(255, bg.Color.B + 15)));
            var pressBg = new SolidColorBrush(Color.FromArgb((byte)Math.Max(0, bg.Color.A - 20), (byte)Math.Max(0, bg.Color.R - 20), (byte)Math.Max(0, bg.Color.G - 20), (byte)Math.Max(0, bg.Color.B - 20)));

            btnBorder.MouseEnter += (_, __) => btnBorder.Background = hoverBg;
            btnBorder.MouseLeave += (_, __) => btnBorder.Background = bg;
            btnBorder.PreviewMouseLeftButtonDown += (_, __) => btnBorder.Background = pressBg;
            btnBorder.PreviewMouseLeftButtonUp += (_, __) => btnBorder.Background = hoverBg;
            btnBorder.MouseLeftButtonUp += (_, __) => onClick();
            return btnBorder;
        }
    }

    public enum DialogIcon { Info, Success, Warning, Error }
    public enum DialogButtons { OK, OKCancel, YesNo, YesNoCancel }
}
