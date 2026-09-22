using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace TsuruLauncher.Controls
{
    /// <summary>
    /// 全应用统一的弹框控件 —— 视觉照搬 Axolotl（选择关闭 Axolotl Launcher 的方式那张图），
    /// 颜色用项目自己的主题（AccentBrush / Surface / TextPrimary，按当前明/暗主题自动切换）。
    ///
    /// Axolotl 风格要素：
    ///   * 标题 = 大粗字（16 SemiBold），不是带图标圆球的标签条 —— 标题本身就是消息
    ///   * ✕ 在卡片右上角
    ///   * 主按钮（品牌色填充）+ 次按钮（幽灵色）**横排在底部**，不贴右边
    ///   * 可选「记住我的选择」复选框（落在按钮上方）
    ///   * 卡片底色 + 边框 + 阴影 = 当前主题层级（自动跟主题切换）
    ///
    /// 用法：
    ///   var ok = iOS26Dialog.Show(this, "确定要删除 Mod \"xxx\" 吗？此操作不可恢复！",
    ///                              "删除 Mod", DialogIcon.Warning, DialogButtons.YesNo);
    ///   if (ok == true) { ... }
    /// </summary>
    public static class iOS26Dialog
    {
        // ============== 公开 API ==============

        public static bool? Show(Window? owner, string message, string title = "提示",
                                 DialogIcon icon = DialogIcon.Info,
                                 DialogButtons buttons = DialogButtons.OK,
                                 string? rememberChoiceText = null,
                                 bool rememberDefaultChecked = false)
        {
            var dlg = BuildDialog(owner, message, title, icon, buttons, rememberChoiceText, rememberDefaultChecked, modeless: false);
            if (dlg == null) return null;
            var ok = dlg.ShowDialog() == true;
            return ok ? true : false;
        }

        public static bool? Show(string message, string title = "提示",
                                 DialogIcon icon = DialogIcon.Info,
                                 DialogButtons buttons = DialogButtons.OK,
                                 string? rememberChoiceText = null,
                                 bool rememberDefaultChecked = false)
            => Show(Application.Current?.MainWindow, message, title, icon, buttons, rememberChoiceText, rememberDefaultChecked);

        /// <summary>自检用：非模态弹出，返回 dialog 供外部截图。生产不要用。</summary>
        public static Window? ShowCore(Window? owner, string message, string title = "提示",
                                       DialogIcon icon = DialogIcon.Info,
                                       DialogButtons buttons = DialogButtons.OK,
                                       string? rememberChoiceText = null,
                                       bool rememberDefaultChecked = false)
            => BuildDialog(owner, message, title, icon, buttons, rememberChoiceText, rememberDefaultChecked, modeless: true);

        // ============== 构建 ==============

        private static Window? BuildDialog(Window? owner, string message, string title,
                                          DialogIcon icon, DialogButtons buttons,
                                          string? rememberChoiceText, bool rememberDefaultChecked,
                                          bool modeless)
        {
            var bg = Utilities.ThemeBrush.Surface3;
            var border = Utilities.ThemeBrush.Border;
            var accent = Utilities.ThemeBrush.Accent;
            var accentFg = Utilities.ThemeBrush.AccentForeground;
            var textPri = Utilities.ThemeBrush.TextPrimary;
            var textSec = Utilities.ThemeBrush.TextSecondary;
            var textDim = Utilities.ThemeBrush.TextTertiary;

            bool rememberChecked = rememberDefaultChecked;

            var dlg = new Window
            {
                Title = "",
                Width = 460,
                WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = owner,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = false,
            };

            var rootGrid = new Grid();
            dlg.Content = rootGrid;

            var cardTransform = new ScaleTransform(0.94, 0.94);
            var translateTransform = new TranslateTransform(0, 10);
            var transformGroup = new TransformGroup();
            transformGroup.Children.Add(cardTransform);
            transformGroup.Children.Add(translateTransform);

            var shadow = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 36,
                ShadowDepth = 6,
                Direction = 270,
                Opacity = 0.18,
                Color = (Color)ColorConverter.ConvertFromString("#000000")!,
            };

            var mainBorder = new Border
            {
                Background = bg,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(0),
                ClipToBounds = true,
                RenderTransform = transformGroup,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Effect = shadow,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            rootGrid.Children.Add(mainBorder);

            // 标题 + ✕
            var titleText = new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = textPri,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24, 22, 48, 8),
            };

            var closeBtn = new Button
            {
                Content = "✕",
                Width = 36,
                Height = 36,
                FontSize = 14,
                Foreground = textDim,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 8, 0),
            };
            closeBtn.MouseEnter += (_, __) => closeBtn.Foreground = textSec;
            closeBtn.MouseLeave += (_, __) => closeBtn.Foreground = textDim;
            closeBtn.Click += (_, __) => CloseDialog(dlg, false, mainBorder, cardTransform, translateTransform);

            var titleRow = new Grid();
            titleRow.Children.Add(titleText);
            titleRow.Children.Add(closeBtn);

            var divider = new Border
            {
                Height = 1,
                Background = Utilities.ThemeBrush.Surface4,
            };

            var messageText = new TextBlock
            {
                Text = message,
                FontSize = 14,
                Foreground = textSec,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(24, 14, 24, 14),
            };

            FrameworkElement? rememberRow = null;
            if (!string.IsNullOrEmpty(rememberChoiceText))
            {
                var cb = new CheckBox
                {
                    Content = rememberChoiceText,
                    IsChecked = rememberChecked,
                    Margin = new Thickness(22, 2, 22, 14),
                    FontSize = 13,
                    Foreground = textSec,
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                cb.Checked += (_, __) => rememberChecked = true;
                cb.Unchecked += (_, __) => rememberChecked = false;
                rememberRow = cb;
            }

            // 按钮行：底部一行，主按钮贴右（Axolotl 顺序：先次后主）
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(24, 6, 24, 22),
            };

            // ⚠ YesNo 也要出「取消」！之前漏了 case，YesNo 落进 default 只生成一个「确认」按钮，
            //   用户想取消只能点右上角 ✕。
            switch (buttons)
            {
                case DialogButtons.OKCancel:
                case DialogButtons.YesNo:
                case DialogButtons.YesNoCancel:
                    btnRow.Children.Add(MakePillButton("取消", textSec, Utilities.ThemeBrush.Surface4,
                        () => CloseDialog(dlg, false, mainBorder, cardTransform, translateTransform), false));
                    btnRow.Children.Add(new Border { Width = 12, Background = System.Windows.Media.Brushes.Transparent });
                    btnRow.Children.Add(MakePillButton(GetConfirmText(buttons), accentFg, accent,
                        () => CloseDialog(dlg, true, mainBorder, cardTransform, translateTransform), true));
                    break;
                default:
                    btnRow.Children.Add(MakePillButton(GetConfirmText(buttons), accentFg, accent,
                        () => CloseDialog(dlg, true, mainBorder, cardTransform, translateTransform), true));
                    break;
            }

            var contentPanel = new StackPanel();
            contentPanel.Children.Add(titleRow);
            contentPanel.Children.Add(divider);
            contentPanel.Children.Add(messageText);
            if (rememberRow != null) contentPanel.Children.Add(rememberRow);
            contentPanel.Children.Add(btnRow);
            mainBorder.Child = contentPanel;

            contentPanel.Measure(new Size(460, double.PositiveInfinity));
            double desiredHeight = Math.Max(140, contentPanel.DesiredSize.Height + 12);
            dlg.Height = desiredHeight;

            dlg.MouseLeftButtonDown += (_, e) => { if (e.Source is Window || e.Source is Grid) dlg.DragMove(); };

            dlg.Loaded += (_, __) =>
            {
                var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.30 };
                var scaleAnim = new DoubleAnimation(0.94, 1.0, TimeSpan.FromMilliseconds(260)) { EasingFunction = spring };
                cardTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnim);
                cardTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnim);
                translateTransform.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            };

            if (modeless)
            {
                dlg.Show();
                return dlg;
            }
            return dlg;
        }

        private static void CloseDialog(Window dlg, bool? result, Border card, ScaleTransform scale, TranslateTransform trans)
        {
            // 非模态窗口（自检用 ShowCore）不能设 DialogResult，会抛 InvalidOperationException。
            try { dlg.DialogResult = result; } catch { /* modeless：靠下面的 Close() 关 */ }
            var sd = new DoubleAnimation(1.0, 0.96, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, sd);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, sd);
            trans.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -6, TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } });

            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            t.Tick += (_, __) => { t.Stop(); dlg.Close(); };
            t.Start();
        }

        private static string GetConfirmText(DialogButtons b) => b switch
        {
            DialogButtons.YesNo or DialogButtons.YesNoCancel => "确认",
            _ => "好",
        };

        private static FrameworkElement MakePillButton(string text, Brush fg, Brush bg, Action onClick, bool isPrimary)
        {
            var btn = new Button
            {
                Content = text,
                Padding = new Thickness(22, 8, 22, 8),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = fg,
                Background = bg,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                MinWidth = 96,
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };

            var hoverBg = Shift(bg, 0.92);
            var pressBg = Shift(bg, 0.85);
            var origin = bg;

            // hover / press 的视觉反馈：用 Preview 事件（Preview 在 ButtonBase 的类处理器之前跑，
            // 不会被 Handled 吃掉）。
            btn.MouseEnter += (_, __) => btn.Background = hoverBg;
            btn.MouseLeave += (_, __) => btn.Background = origin;
            btn.PreviewMouseLeftButtonDown += (_, __) => btn.Background = pressBg;
            btn.PreviewMouseLeftButtonUp += (_, __) => btn.Background = hoverBg;

            // ⚠ 必须用 Click 而不是 MouseLeftButtonUp：
            //   ButtonBase 的类处理器在 OnMouseLeftButtonUp 里会把事件标成 Handled=true，
            //   于是通过 `+=` 挂的实例处理器（没带 handledEventsToo）根本不会被调用 ——
            //   表现就是「按钮点了没反应」。用 Click 走 Button 自己的语义最稳。
            btn.Click += (_, __) => onClick();

            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            var contentPresenter = new FrameworkElementFactory(typeof(ContentPresenter));
            contentPresenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            contentPresenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            borderFactory.AppendChild(contentPresenter);
            template.VisualTree = borderFactory;
            btn.Template = template;

            return btn;

            static Brush Shift(Brush b, double factor)
            {
                if (b is SolidColorBrush scb)
                {
                    var c = scb.Color;
                    return new SolidColorBrush(Color.FromRgb(
                        (byte)(c.R * factor), (byte)(c.G * factor), (byte)(c.B * factor)));
                }
                return b;
            }
        }
    }

    public enum DialogIcon { Info, Success, Warning, Error }
    public enum DialogButtons { OK, OKCancel, YesNo, YesNoCancel }
}