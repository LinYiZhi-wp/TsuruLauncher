using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using TsuruLauncher.Services.Animation;
using System.Windows.Media.Animation;
using System.Windows.Navigation;
using TsuruLauncher.Models;
using TsuruLauncher.Services;
using Microsoft.Win32;

namespace TsuruLauncher.Views
{
    public partial class VersionSelectorPage : Page
    {
        private VersionDetectionService _detectionService;
        private List<GameDirectory> _directories;
        private List<GameVersion> _allVersions;
        private GameVersion? _selectedVersion;
        private Border? _highlightedBorder;
        private Action<GameVersion> _onVersionSelected;

        public VersionSelectorPage(Action<GameVersion> onVersionSelected)
        {
            InitializeComponent();
            _onVersionSelected = onVersionSelected;
            _detectionService = new VersionDetectionService();
            _directories = new List<GameDirectory>();
            _allVersions = new List<GameVersion>();
            
            LoadDirectories();
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (NavigationService.CanGoBack)
            {
                NavigationService.GoBack();
            }
            else
            {
                // Fallback: Navigate specifically to HomePage if history is empty
                NavigationService.Navigate(new HomePage());
            }
        }

        private void LoadDirectories()
        {
            // 文件夹列表现在由右侧信息栏的 Controls.VersionFolderPanel 承载，
            // 它自己探测目录、自己渲染；选中后通过静态事件回来触发版本重载。
            Controls.VersionFolderPanel.FolderSelected -= OnFolderSelected;
            Controls.VersionFolderPanel.FolderSelected += OnFolderSelected;

            // 右栏那块的可见性/内容由外壳 SyncInfoPanelPageContent 负责，这里不碰。
        }

        private void OnFolderSelected(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            LoadVersionsForDirectory(path);
        }

        private void LoadVersionsForDirectory(string gamePath)
        {
            _allVersions = _detectionService.DetectVersions(gamePath);
            DisplayVersionsByCategory();
        }

        private void DisplayVersionsByCategory()
        {
            VersionsPanel.Children.Clear();

            var moddableVersions = _allVersions.Where(v => v.Category == VersionCategory.Moddable).ToList();
            var vanillaVersions = _allVersions.Where(v => v.Category == VersionCategory.Vanilla).ToList();
            var brokenVersions = _allVersions.Where(v => v.Category == VersionCategory.Broken).ToList();

            if (moddableVersions.Count > 0) AddVersionCategory($"可装 Mod ({moddableVersions.Count})", moddableVersions);
            if (vanillaVersions.Count > 0) AddVersionCategory($"常规版本 ({vanillaVersions.Count})", vanillaVersions);
            if (brokenVersions.Count > 0) AddVersionCategory($"错误的版本 ({brokenVersions.Count})", brokenVersions);

            if (_allVersions.Count == 0)
            {
                var noVersionsText = new TextBlock
                {
                    Text = "未检测到任何版本\n请先下载或安装游戏版本",
                    FontSize = 14, Opacity = 0.6, Foreground = Utilities.ThemeBrush.TextPrimary,
                    HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 50, 0, 0)
                };
                VersionsPanel.Children.Add(noVersionsText);
            }
        }

        private void AddVersionCategory(string categoryName, List<GameVersion> versions)
        {
            var categoryHeader = new Border
            {
                Background = Utilities.ThemeBrush.Surface4,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(15, 12, 15, 12),
                Margin = new Thickness(0, 10, 0, 10)
            };
            categoryHeader.Child = new TextBlock
            {
                Text = categoryName, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Utilities.ThemeBrush.TextPrimary
            };
            VersionsPanel.Children.Add(categoryHeader);

            foreach (var version in versions)
            {
                VersionsPanel.Children.Add(CreateVersionItem(version));
            }
        }

        private Border CreateVersionItem(GameVersion version)
        {
            // 视觉：轻量卡片（Surface4 底 + 1px 描边 + 圆角 12），
            // 左侧图标放进圆角底衬里（不再让 emoji 裸奔），右侧「选择」用**描边**按钮而不是实心绿块
            // —— 之前每行一个实心 Accent 按钮，一整屏绿块，观感很差。
            var card = new Border
            {
                Background = Utilities.ThemeBrush.Surface4,
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 12, 12, 12),
                Margin = new Thickness(0, 0, 0, 10),
                BorderBrush = Utilities.ThemeBrush.Border,
                BorderThickness = new Thickness(1),
                Tag = version,
                Cursor = Cursors.Hand
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 图标
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // 文字
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 选择
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });   // 设置

            // ① 图标底衬
            var iconBox = new Border
            {
                Width = 42, Height = 42,
                CornerRadius = new CornerRadius(11),
                Background = Utilities.ThemeBrush.AccentAlpha(26),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = version.Icon,
                    FontSize = 20,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            Grid.SetColumn(iconBox, 0);
            grid.Children.Add(iconBox);

            // ② 名称 + 详情
            var info = new StackPanel { Margin = new Thickness(14, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock
            {
                Text = version.DisplayName,
                FontSize = 15.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Utilities.ThemeBrush.TextPrimary,
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            string details = version.Type == VersionType.Release ? "正式版" : "快照版";
            if (!string.IsNullOrEmpty(version.MinecraftVersion)) details += $"  ·  {version.MinecraftVersion}";
            if (version.Loader != null) details += $"  ·  {version.Loader} {version.LoaderVersion}";
            info.Children.Add(new TextBlock
            {
                Text = details,
                FontSize = 11.5,
                Foreground = Utilities.ThemeBrush.TextTertiary,
                Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(info, 1);
            grid.Children.Add(info);

            // ③ 「选择」——描边按钮（透明底 + 1px 边），hover 才填色。
            //    永远可见，所以用户一眼知道点哪里；但不抢视线。
            var selectBtn = new Button
            {
                Content = "选择",
                Padding = new Thickness(16, 7, 16, 7),
                MinWidth = 66,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Utilities.ThemeBrush.TextPrimary,
                Background = System.Windows.Media.Brushes.Transparent,
                BorderBrush = Utilities.ThemeBrush.Border,
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "select",
                Template = BuildOutlineButtonTemplate()
            };
            // 缩放动画：悬停弹一下 / 按下回缩 / 离开复位（Axolotl 那套手感）
            var selectScale = new ScaleTransform(1, 1);
            selectBtn.RenderTransformOrigin = new Point(0.5, 0.5);
            selectBtn.RenderTransform = selectScale;
            selectBtn.MouseEnter += (a, b) => AnimateScale(selectScale, 1.05, 180, AxolotlMotion.OvershootEase);
            selectBtn.MouseLeave += (a, b) => AnimateScale(selectScale, 1.0, 160, AxolotlMotion.EaseOut);
            selectBtn.PreviewMouseDown += (a, b) => AnimateScale(selectScale, 0.96, 90, null);
            selectBtn.PreviewMouseUp += (a, b) => AnimateScale(selectScale, 1.05, 140, AxolotlMotion.OvershootEase);

            selectBtn.Click += (s, e) =>
            {
                e.Handled = true;
                HighlightSelectedVersion(card);
                _selectedVersion = version;
                ConfirmSelection();
            };
            Grid.SetColumn(selectBtn, 2);
            grid.Children.Add(selectBtn);

            // ④ ⚙ 设置
            var settingsButton = new Button
            {
                Width = 34, Height = 34,
                Background = System.Windows.Media.Brushes.Transparent,
                Foreground = Utilities.ThemeBrush.TextTertiary,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "此版本的设置",
                Content = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Settings24, FontSize = 15 },
                Template = BuildCircleIconTemplate()
            };
            settingsButton.Click += (s, e) =>
            {
                e.Handled = true;
                OpenVersionSettings(version);
            };
            Grid.SetColumn(settingsButton, 3);
            grid.Children.Add(settingsButton);

            card.Child = grid;

            // 悬停：整卡浅抬亮（不改描边，避免和「已选中」混淆）
            card.MouseEnter += (s, e) =>
            {
                if (!ReferenceEquals(card, _highlightedBorder))
                    card.Background = Utilities.ThemeBrush.AccentAlpha(16);
            };
            card.MouseLeave += (s, e) =>
            {
                if (!ReferenceEquals(card, _highlightedBorder))
                    card.Background = Utilities.ThemeBrush.Surface4;
            };

            // **单击整行 = 选中并确认**（不再要求双击；「选择」按钮走同一条路）
            card.MouseLeftButtonUp += (s, e) =>
            {
                if (e.OriginalSource is DependencyObject dep && IsChildInteractive(dep)) return;
                HighlightSelectedVersion(card);
                _selectedVersion = version;
                ConfirmSelection();
                e.Handled = true;
            };

            return card;
        }

        /// <summary>
        /// 「选择」按钮的模板：透明底 + 1px 描边，hover / 按下才填充。
        /// 用模板而不是直接设 Background —— 这样 hover 态能走 DynamicResource，主题切换不会漏。
        /// </summary>
        /// <summary>
        /// 「选择」按钮的模板 —— 要"灵动"：
        ///   * 常态：透明底 + 1px 描边（不抢视线）
        ///   * 悬停：180ms 填 Accent + 白字，同时**弹一下**（scale 1 → 1.05，过冲曲线）
        ///   * 按下：scale 回缩到 0.96（有"按下去"的实感）
        ///   * 离开：160ms 淡回描边态
        /// 缩放走 RenderTransform 的 ScaleTransform（由模板工厂建、可被 Storyboard 按名字定位）。
        /// </summary>
        /// <summary>圆形图标按钮（⚙）：透明底，悬停淡出一层 accent 圆底 + 图标变色，150ms。</summary>
        private static ControlTemplate BuildCircleIconTemplate()
        {
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(17));
            border.SetValue(Border.BackgroundProperty, System.Windows.Media.Brushes.Transparent);
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;

            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Utilities.ThemeBrush.AccentAlpha(30), "Bd"));
            hover.Setters.Add(new Setter(Control.ForegroundProperty, Utilities.ThemeBrush.Accent));
            template.Triggers.Add(hover);
            return template;
        }

        private static ControlTemplate BuildOutlineButtonTemplate()
        {
            var template = new ControlTemplate(typeof(Button));

            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Bd";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;

            // 悬停：180ms 填 Accent + 白字（缩放动画在 CreateVersionItem 里直接对 RenderTransform 做 ——
            // FrameworkElementFactory **不接受 Freezable**（ScaleTransform 会抛
            // "类型必须从 FrameworkElement…派生"），所以缩放不能写在模板里）
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Utilities.ThemeBrush.Accent, "Bd"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, Utilities.ThemeBrush.Accent, "Bd"));
            hover.Setters.Add(new Setter(Control.ForegroundProperty, Utilities.ThemeBrush.AccentForeground));
            template.Triggers.Add(hover);

            var pressed = new Trigger { Property = Button.IsPressedProperty, Value = true };
            pressed.Setters.Add(new Setter(Border.BackgroundProperty, Utilities.ThemeBrush.Accent, "Bd"));
            pressed.Setters.Add(new Setter(Control.ForegroundProperty, Utilities.ThemeBrush.AccentForeground));
            template.Triggers.Add(pressed);

            return template;
        }

        /// <summary>对某个 ScaleTransform 做一段缩放动画（弹一下 / 回缩）。</summary>
        private static void AnimateScale(ScaleTransform scale, double to, int ms, IEasingFunction? ease)
        {
            var sx = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            var sy = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, sx);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, sy);
        }

        /// <summary>圆形图标按钮（⚙）：透明底，悬停淡出一层 accent 圆底 + 图标变色，150ms。</summary>
        // 防止点了「选择」或「⚙」按钮时，事件冒泡再触发整行的选中
        private static bool IsChildInteractive(DependencyObject d)
        {
            while (d != null)
            {
                if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
                d = System.Windows.Media.VisualTreeHelper.GetParent(d);
            }
            return false;
        }

        private void HighlightSelectedVersion(Border selectedBorder)
        {
            foreach (var child in VersionsPanel.Children)
            {
                if (child is Border border && border.Tag is GameVersion)
                {
                    border.Background = Utilities.ThemeBrush.Surface4;
                    border.BorderBrush = Utilities.ThemeBrush.Border;
                    border.BorderThickness = new Thickness(1);
                }
            }
            selectedBorder.Background = Utilities.ThemeBrush.AccentAlpha(30);
            selectedBorder.BorderBrush = Utilities.ThemeBrush.Accent;
            selectedBorder.BorderThickness = new Thickness(1.5);
            _highlightedBorder = selectedBorder;
        }

        private void ConfirmSelection()
        {
            if (_selectedVersion != null)
            {
                _onVersionSelected?.Invoke(_selectedVersion);
                if (NavigationService.CanGoBack) NavigationService.GoBack();
            }
        }

        private void OpenVersionSettings(GameVersion version)
        {
            var gameInstance = new GameInstance
            {
                Id = version.Id,
                RootPath = version.GamePath,
                GameDir = version.GamePath,
                Type = version.Type.ToString().ToLower()
            };
            NavigationService?.Navigate(new VersionSettingsPage(gameInstance));
        }
    }
}
