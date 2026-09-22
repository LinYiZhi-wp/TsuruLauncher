using System;
using TsuruLauncher.Controls;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using System.IO.Compression;
using TsuruLauncher.ViewModels;
using TsuruLauncher.Models;
using TsuruLauncher.Services.Animation;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using TsuruLauncher.Services;

namespace TsuruLauncher.Views
{
    public partial class VersionSettingsPage : Page
    {
        private GameInstance _version;
        private VersionSettings? _settings;
        private string _startTab = "overview";
        private string _customJvmArgs = string.Empty;
        private TextBox? _jvmArgsBox;
        private ComboBox? _javaCombo;

        public VersionSettingsPage(GameInstance version)
        {
            InitializeComponent();
            _version = version;
            InitializePage();
        }

        public VersionSettingsPage(GameInstance version, string startTab) : this(version)
        {
            _startTab = startTab;
        }

        private void InitializePage()
        {
            _settings = LoadVersionSettings(_version.Id);
            
            SubtitleText.Text = _version.Id;
            
            if (_startTab == "mods")
            {
                 Dispatcher.BeginInvoke(() => TabButton_Click(TabMods, new RoutedEventArgs()));
            }
            else
            {
                 ShowOverviewTab();
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            SaveVersionSettings();
            if (NavigationService.CanGoBack) NavigationService.GoBack();
        }



        private VersionSettings LoadVersionSettings(string versionId)
        {
            var settings = new VersionSettings
            {
                VersionId = versionId,
                CustomName = _version.Id,
                VersionIsolation = true,
                MemoryMode = MemoryAllocation.Auto,
                MinMemoryMB = 512,
                MaxMemoryMB = 4096
            };

            try
            {
                string configPath = Services.GameService.ResolveProfilePath(_version.GameDir);
                if (File.Exists(configPath))
                {
                    var json = JObject.Parse(File.ReadAllText(configPath));
                    if (json.ContainsKey("UseGlobalSettings"))
                        settings.MemoryMode = (bool?)json["UseGlobalSettings"] == false
                            ? MemoryAllocation.Custom
                            : MemoryAllocation.FollowGlobal;
                    if (json.ContainsKey("CustomMemoryMb")) settings.MaxMemoryMB = (int?)json["CustomMemoryMb"] ?? 4096;
                    if (json.ContainsKey("CustomMinMemoryMb")) settings.MinMemoryMB = (int?)json["CustomMinMemoryMb"] ?? 512;
                    if (json.ContainsKey("CustomJavaPath")) settings.JavaPath = json["CustomJavaPath"]?.ToString() ?? "";
                    if (json.ContainsKey("CustomJvmArgs")) _customJvmArgs = json["CustomJvmArgs"]?.ToString() ?? "";
                }
            }
            catch { }

            return settings;
        }

        private void SaveVersionSettings()
        {
            try
            {
                var json = new JObject();
                json["UseGlobalSettings"] = _settings?.MemoryMode == MemoryAllocation.Custom ? false : true;
                json["CustomMemoryMb"] = _settings?.MaxMemoryMB ?? 4096;
                json["CustomMinMemoryMb"] = _settings?.MinMemoryMB ?? 512;
                json["CustomJavaPath"] = _settings?.JavaPath ?? "";
                json["CustomJvmArgs"] = _customJvmArgs ?? "";

                string configPath = Path.Combine(_version.GameDir, Services.GameService.ProfileFileName);
                File.WriteAllText(configPath, json.ToString());

                // 迁移：删掉 v1.x 的旧文件名，避免下次又读到旧数据
                try
                {
                    string legacyPath = Path.Combine(_version.GameDir, Services.GameService.LegacyProfileFileName);
                    if (File.Exists(legacyPath)) File.Delete(legacyPath);
                }
                catch { }
            }
            catch { }
        }

        /// <summary>当前已经显示过的标签；重复点同一个标签直接吞掉（旧实现靠 IsEnabled=false 做到这点）。</summary>
        private string? _currentTabTag;

        private void TabButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton button && button.Tag is string tag)
            {
                // 选中态 = 容器里的纵向滑动指示块（seg:SegmentedIndicator Axis=Vertical），
                // 这里只负责把 GroupName 里的选中项切过去 + 换内容。
                if (!button.IsChecked.GetValueOrDefault())
                    button.IsChecked = true;

                if (string.Equals(_currentTabTag, tag, StringComparison.Ordinal)) return;

                switch (tag)
                {
                    case "overview": SwitchToTab(tag, () => ShowOverviewTab()); break;
                    case "settings": SwitchToTab(tag, () => ShowSettingsTab()); break;
                    case "mods": SwitchToTab(tag, () => ShowModsTab()); break;
                    case "export": SwitchToTab(tag, () => ShowExportTab()); break;
                    case "maintenance": SwitchToTab(tag, () => ShowMaintenanceTab()); break;
                }
            }
        }

        /// <summary>
        /// 标签切换：① 先清空内容区（保证不残留上一个标签的内容）→ ② 构建新内容 →
        /// ③ 用 Axolotl 的内容淡入进场（进入 180ms ease + 28ms 步长 / 168ms 上限的错峰，
        /// 起点 translateY(-6px)，见 AXOLOTL_MOTION_PARAMS.md §1 / §7）。
        ///
        /// 构建异常不再被吞掉：写进 TsuruLauncher.log —— 之前这里出错只会得到一个空白标签页。
        /// 另外，<paramref name="loadAction"/> 里的 Show...Tab 方法必须自己把内容 Add 进
        /// <see cref="ContentArea"/>；只 return 一个 UIElement 而不 Add 的话，
        /// 因为这里是 <see cref="Action"/>，返回值会被直接丢掉。
        /// </summary>
        private void SwitchToTab(string tag, Action loadAction)
        {
            _currentTabTag = tag;
            ContentArea.Children.Clear();

            try
            {
                loadAction();
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "VersionSettingsPage.SwitchToTab(" + tag + ")");
            }

            if (ContentArea.Children.Count == 0)
            {
                Utilities.Logger.LogWarning("[VersionSettingsPage] 标签内容为空，回退到提示卡 tab=" + tag);
                ContentArea.Children.Add(CreateNotice("这个标签页暂时没有可显示的内容。", Themed("WarningBrush")));
            }

            int animated = 0;
            foreach (UIElement content in ContentArea.Children)
            {
                if (content is not FrameworkElement element) continue;
                PageTransition.PlayContentFadeIn(element, staggerChildren: true);
                if (PageTransition.IsAnimating(element)) animated++;
            }

            // 每次切换都留一行证据：内容块数量 + 淡入时钟是否真的起来了（排查「点了没反应」用）
            Utilities.Logger.LogInfo("[VersionSettingsPage] tab=" + tag
                + " roots=" + ContentArea.Children.Count
                + " animated=" + animated
                + " enter=opacity " + AxolotlMotion.PageEnterFadeMs + "ms ease"
                + " + scale " + AxolotlMotion.ContentSwitchFromScale + "->1"
                + " + translateY " + AxolotlMotion.ContentSwitchOffsetPx + "px->0"
                + " over " + AxolotlMotion.ContentSwitchMs + "ms cubic-bezier(0.15,1.4,0.64,0.96)"
                + " stagger=" + AxolotlMotion.StaggerStepMs + "ms/" + AxolotlMotion.StaggerCapMs + "ms"
                + " (was pure-fade 180ms, no move)"
                + " motion=" + PageTransition.AnimationsEnabled);
        }

        private void ShowMaintenanceTab()
        {
            ContentArea.Children.Clear();
            StackPanel stackPanel = new StackPanel();
            stackPanel.Children.Add(CreateSectionHeader("版本维护"));
            Border border = new Border
            {
                Background = Themed("Surface4Brush"),
                CornerRadius = new CornerRadius(20.0),
                Padding = new Thickness(20.0),
                Margin = new Thickness(0.0, 0.0, 0.0, 20.0)
            };
            StackPanel stackPanel2 = new StackPanel();
            stackPanel2.Children.Add(new TextBlock
            {
                Text = "⚠\ufe0f 危险操作区",
                Foreground = Utilities.ThemeBrush.Get("WarningBrush"),
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
            });
            stackPanel2.Children.Add(CreateActionButton("\ud83d\uddd1\ufe0f 删除该版本", async delegate
            {
                await DeleteVersionAsync();
            }, "#30FF0000"));
            stackPanel2.Children.Add(CreateActionButton("\ud83d\udcc2 打开版本文件夹", delegate
            {
                OpenFolder(_version.GameDir);
            }));
            border.Child = stackPanel2;
            stackPanel.Children.Add(border);
            ContentArea.Children.Add(stackPanel);
        }
    
        private void ShowExportTab()
        {
            ContentArea.Children.Clear();
            StackPanel stackPanel = new StackPanel();
            Border border = new Border
            {
                Background = Themed("Surface4Brush"),
                CornerRadius = new CornerRadius(20.0),
                Padding = new Thickness(20.0),
                Margin = new Thickness(0.0, 0.0, 0.0, 20.0)
            };
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1.0, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(100.0)
            });
            grid.Children.Add(new TextBlock
            {
                Text = GetString("Export_ModpackName"),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Utilities.ThemeBrush.TextPrimary,
                Margin = new Thickness(0.0, 0.0, 10.0, 0.0)
            });
            TextBox nameBox = CreateTextBox(_version.Id);
            Grid.SetColumn(nameBox, 1);
            grid.Children.Add(nameBox);
            TextBlock element = new TextBlock
            {
                Text = GetString("Export_ModpackVersion"),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Utilities.ThemeBrush.TextPrimary,
                Margin = new Thickness(20.0, 0.0, 10.0, 0.0)
            };
            Grid.SetColumn(element, 2);
            grid.Children.Add(element);
            TextBox verBox = CreateTextBox("1.0.0");
            Grid.SetColumn(verBox, 3);
            grid.Children.Add(verBox);
            border.Child = grid;
            stackPanel.Children.Add(border);
            stackPanel.Children.Add(CreateSectionHeader(GetString("Export_Section_Content")));
            StackPanel stackPanel2 = new StackPanel();
            CheckBox chkGameCore = CreateCheckbox($"{GetString("Export_GameCore")} {_version.Type} {_version.Id}", isChecked: true, isEnabled: false);
            CheckBox chkSettings = CreateCheckbox(GetString("Export_GameSettings"), isChecked: true);
            CheckBox chkSaves = CreateCheckbox(GetString("Export_Saves"), isChecked: false);
            stackPanel2.Children.Add(chkGameCore);
            stackPanel2.Children.Add(chkSettings);
            stackPanel2.Children.Add(chkSaves);
            Border element2 = new Border
            {
                Background = Themed("Surface4Brush"),
                CornerRadius = new CornerRadius(20.0),
                Padding = new Thickness(20.0),
                Child = stackPanel2,
                Margin = new Thickness(0.0, 0.0, 0.0, 20.0)
            };
            stackPanel.Children.Add(element2);
            Button startBtn = new Button
            {
                Content = GetString("Export_Button_Start"),
                Padding = new Thickness(30.0, 10.0, 30.0, 10.0),
                Background = Themed("AccentBrush"),
                Foreground = Utilities.ThemeBrush.TextPrimary,
                FontSize = 16.0,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand,
                Style = (Style)FindResource(typeof(Button))
            };
            ProgressBar progressBar = new ProgressBar
            {
                Height = 4.0,
                Margin = new Thickness(0.0, 10.0, 0.0, 0.0),
                Visibility = Visibility.Collapsed,
                IsIndeterminate = true,
                Foreground = Themed("SuccessBrush")
            };
            startBtn.Click += async delegate
            {
                SaveFileDialog saveFileDialog = new SaveFileDialog
                {
                    Filter = "Zip Archive (*.zip)|*.zip",
                    FileName = nameBox.Text + "-" + verBox.Text + ".zip"
                };
                if (saveFileDialog.ShowDialog() == true)
                {
                    startBtn.IsEnabled = false;
                    startBtn.Content = GetString("Export_Exporting");
                    progressBar.Visibility = Visibility.Visible;
                    try
                    {
                        await GameExportService.ExportGameAsync(options: new ExportOptions
                        {
                            ExportPath = saveFileDialog.FileName,
                            ModpackName = nameBox.Text,
                            ModpackVersion = verBox.Text,
                            IncludeGameCore = (chkGameCore.IsChecked == true),
                            IncludeGameSettings = (chkSettings.IsChecked == true),
                            IncludeSaves = (chkSaves.IsChecked == true),
                            IncludeMods = true,
                            IncludeResourcePacks = true,
                            IncludeShaderPacks = true
                        }, game: _version, progress: null);
                        iOS26Dialog.Show(GetString("Export_Success_Message"), GetString("Export_Success_Title"), DialogIcon.Info, DialogButtons.OK);
                    }
                    catch (Exception ex2)
                    {
                        Exception ex = ex2;
                        iOS26Dialog.Show("导出失败: " + ex.Message, GetString("Export_Error_Title"), DialogIcon.Error, DialogButtons.OK);
                    }
                    finally
                    {
                        startBtn.IsEnabled = true;
                        startBtn.Content = GetString("Export_Button_Start");
                        progressBar.Visibility = Visibility.Collapsed;
                    }
                }
            };
            StackPanel stackPanel3 = new StackPanel
            {
                Margin = new Thickness(0.0, 20.0, 0.0, 0.0)
            };
            stackPanel3.Children.Add(startBtn);
            stackPanel3.Children.Add(progressBar);
            stackPanel.Children.Add(stackPanel3);
            ContentArea.Children.Add(stackPanel);
        }
    
        // --- 内容管理（模组 / 资源包 / 光影 / 世界 / 截图 / 日志） ---
        private string _contentCategory = "mods";

        private static readonly (string Key, string Label, string Dir, string Hint)[] ContentCategories =
        {
            ("mods", "模组", "mods", "安装、启停与清理 Mod（Fabric / Forge / NeoForge 等加载器均可）"),
            ("resourcepacks", "资源包", "resourcepacks", "材质、声音与字体，游戏内「选项 → 资源包」中启用"),
            ("shaderpacks", "光影", "shaderpacks", "需要 Iris / OptiFine 等光影加载器才会生效"),
            ("worlds", "世界", "saves", "存档文件夹，删除前请先备份重要进度"),
            ("screenshots", "截图", "screenshots", "游戏内按 F2 截图，点击可在默认程序中打开"),
            ("logs", "日志", "logs", "崩溃与运行日志，排查问题时先看 latest.log")
        };

        private void ShowModsTab()
        {
            ShowContentTab("mods");
        }
    
        private string GetContentPath(string key)
        {
            string path = (string.IsNullOrEmpty(_version.GameDir) ? _version.RootPath : _version.GameDir);
            string text = null;
            (string, string, string, string)[] contentCategories = ContentCategories;
            for (int i = 0; i < contentCategories.Length; i++)
            {
                (string, string, string, string) tuple = contentCategories[i];
                if (tuple.Item1 == key)
                {
                    text = tuple.Item3;
                }
            }
            return Path.Combine(path, string.IsNullOrEmpty(text) ? key : text);
        }
    
        private static Brush Themed(string key)
        {
            return Themed(key, Color.FromRgb(136, 140, 148));
        }
    
        private static Brush Themed(string key, Color fallback)
        {
            Brush brush = ((Application.Current == null) ? null : (Application.Current.TryFindResource(key) as Brush));
            return brush ?? new SolidColorBrush(fallback);
        }
    
        private static SolidColorBrush Argb(byte a, byte r, byte g, byte b)
        {
            return new SolidColorBrush(Color.FromArgb(a, r, g, b));
        }
    
        private static bool IsContentFile(string file)
        {
            string fileName = Path.GetFileName(file);
            if (fileName.StartsWith(".", StringComparison.Ordinal))
            {
                return false;
            }
            return !fileName.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".part", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".download", StringComparison.OrdinalIgnoreCase);
        }
    
        private int CountContent(string key)
        {
            try
            {
                string contentPath = GetContentPath(key);
                if (!Directory.Exists(contentPath))
                {
                    return 0;
                }
                if (key == "worlds")
                {
                    return Directory.GetDirectories(contentPath).Length;
                }
                int num = 0;
                string[] files = Directory.GetFiles(contentPath);
                foreach (string file in files)
                {
                    if (IsContentFile(file) && MatchesCategory(file, key))
                    {
                        num++;
                    }
                }
                return num;
            }
            catch
            {
                return 0;
            }
        }
    
        private static bool MatchesCategory(string file, string key)
        {
            string text = Path.GetFileName(file).ToLowerInvariant();
            string text2 = (text.EndsWith(".disabled") ? text.Substring(0, text.Length - 9) : text);
            return key switch
            {
                "mods" => text2.EndsWith(".jar") || text2.EndsWith(".zip"), 
                "resourcepacks" => text2.EndsWith(".zip") || text2.EndsWith(".jar"), 
                "shaderpacks" => text2.EndsWith(".zip"), 
                "screenshots" => text2.EndsWith(".png") || text2.EndsWith(".jpg") || text2.EndsWith(".jpeg"), 
                "logs" => text2.EndsWith(".log") || text2.EndsWith(".txt") || text2.EndsWith(".log.gz"), 
                _ => true, 
            };
        }
    
        // --- 内容分类胶囊行：**常驻复用**，不随内容重建 -------------------------------------
        //
        // 为什么不能每次重建：滑动指示块（seg:SegmentedIndicator）在「首次布局」时是直接落位、
        // 不播动画的（日志里是 skipped reason=load）。以前这一行是每次 ShowContentTab 都新建，
        // 于是每次都走首次布局 —— 切换分类时指示块只会瞬移，观感就是「动画很丑、一点都不灵动」。
        // 现在整行只建一次并缓存，切换时只更新计数与选中态，指示块就能正常滑过去。
        //
        // 顺带修掉的两个问题（都在旧实现里）：
        //   * 旧的是普通 Button，**没有 CornerRadius** —— 在一堆圆角里杵着一个直角方块，
        //     就是用户说的「既有圆角又有直角」；现在用 SegTab（圆角 10 + 1px GlassBorderBrush）。
        //   * 旧代码硬编码了 Brushes.Transparent 与 Argb(40,128,128,128)，违反设计红线
        //     （颜色一律 DynamicResource）；SegTab 全部走 DynamicResource。
        private Grid? _contentPillRow;
        private readonly List<RadioButton> _contentPillButtons = new List<RadioButton>();

        private Grid BuildContentPillRow(string activeKey)
        {
            if (_contentPillRow == null)
            {
                Border indicator = new Border { CornerRadius = new CornerRadius(10), Opacity = 0.0 };
                indicator.SetResourceReference(Border.BackgroundProperty, "AccentBrush");

                Canvas canvas = new Canvas { IsHitTestVisible = false };
                canvas.Children.Add(indicator);

                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
                Style pillStyle = (Style)FindResource("SegTab");
                foreach ((string Key, string Label, string Dir, string Hint) item in ContentCategories)
                {
                    string key = item.Key;
                    RadioButton pill = new RadioButton
                    {
                        Style = pillStyle,
                        GroupName = "VersionContentCategory",
                        Tag = key,
                        ToolTip = item.Hint
                    };
                    pill.Click += delegate { ShowContentTab(key); };
                    _contentPillButtons.Add(pill);
                    row.Children.Add(pill);
                }

                _contentPillRow = new Grid { Margin = new Thickness(0.0, 0.0, 0.0, 16.0) };
                TsuruLauncher.Controls.SegmentedIndicator.SetHost(_contentPillRow, true);
                TsuruLauncher.Controls.SegmentedIndicator.SetAxis(_contentPillRow, TsuruLauncher.Controls.SegmentAxis.Both);
                TsuruLauncher.Controls.SegmentedIndicator.SetIndicator(_contentPillRow, indicator);
                _contentPillRow.Children.Add(canvas);
                _contentPillRow.Children.Add(row);
            }
            else if (_contentPillRow.Parent is Panel oldParent)
            {
                // 上一次的内容区被 Clear 了，但这一行还挂在旧的父容器里 —— 先摘出来再复用。
                oldParent.Children.Remove(_contentPillRow);
            }

            // 计数 + 选中态：选中项一变，指示块就会从旧位置滑过去。
            for (int i = 0; i < _contentPillButtons.Count && i < ContentCategories.Length; i++)
            {
                (string Key, string Label, string Dir, string Hint) item = ContentCategories[i];
                RadioButton pill = _contentPillButtons[i];
                pill.Content = item.Label + " · " + CountContent(item.Key);
                pill.IsChecked = string.Equals(item.Key, activeKey, StringComparison.Ordinal);
            }

            return _contentPillRow;
        }

        private void ShowContentTab(string category)
        {
            string category2 = category;
            _contentCategory = category2;
            ContentArea.Children.Clear();
            StackPanel stackPanel = new StackPanel
            {
                Margin = new Thickness(0.0, 0.0, 0.0, 24.0)
            };
            string path = GetContentPath(category2);
            string text = category2;
            string toolTip = string.Empty;
            (string, string, string, string)[] contentCategories = ContentCategories;
            for (int i = 0; i < contentCategories.Length; i++)
            {
                (string, string, string, string) tuple = contentCategories[i];
                if (tuple.Item1 == category2)
                {
                    text = tuple.Item2;
                    toolTip = tuple.Item4;
                }
            }
            stackPanel.Children.Add(BuildContentPillRow(category2));
            Grid grid = new Grid
            {
                Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1.0, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            StackPanel stackPanel3 = new StackPanel();
            stackPanel3.Children.Add(new TextBlock
            {
                Text = text + "管理",
                FontSize = 20.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = Themed("TextPrimaryBrush")
            });
            stackPanel3.Children.Add(new TextBlock
            {
                Text = path,
                FontSize = 11.0,
                Foreground = Themed("TextTertiaryBrush"),
                Margin = new Thickness(0.0, 3.0, 0.0, 0.0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = toolTip
            });
            grid.Children.Add(stackPanel3);
            StackPanel stackPanel4 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            Grid.SetColumn(stackPanel4, 1);
            if (category2 == "mods" || category2 == "resourcepacks" || category2 == "shaderpacks")
            {
                stackPanel4.Children.Add(CreateActionButton("⬇\ufe0f 获取资源", delegate
                {
                    if (Application.Current.MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.NavigateTo("download");
                    }
                }));
            }
            if (category2 == "logs")
            {
                string latest = Path.Combine(path, "latest.log");
                stackPanel4.Children.Add(CreateActionButton("\ud83d\udcc4 打开 latest.log", delegate
                {
                    if (File.Exists(latest))
                    {
                        OpenPath(latest);
                    }
                    else
                    {
                        iOS26Dialog.Show("还没有 latest.log，先启动一次游戏吧。", "提示", DialogIcon.Info, DialogButtons.OK);
                    }
                }));
            }
            stackPanel4.Children.Add(CreateActionButton("\ud83d\udd04 刷新", delegate
            {
                ShowContentTab(_contentCategory);
            }));
            stackPanel4.Children.Add(CreateActionButton("\ud83d\udcc2 打开文件夹", delegate
            {
                OpenFolder(path);
            }));
            grid.Children.Add(stackPanel4);
            stackPanel.Children.Add(grid);
            bool flag2 = !string.Equals(_version.GameDir, _version.RootPath, StringComparison.OrdinalIgnoreCase);
            stackPanel.Children.Add(CreateNotice(flag2 ? "\ud83d\udccc 版本隔离已开启：这里的文件只属于当前版本，改动不影响其它版本。" : "\ud83c\udf10 版本隔离已关闭：这里的文件被所有版本共享，需要按版本分开管理请在「设置」中开启版本隔离。", flag2 ? Themed("InfoBrush") : Themed("WarningBrush")));
            if (category2 == "mods" && !InspectForModLoader())
            {
                stackPanel.Children.Add(CreateNotice("⚠ 当前版本看起来没有安装 Forge / Fabric / NeoForge 等 Mod 加载器，放到这里的 Mod 不会被加载。请在下载页面安装带加载器的版本，或切换到一个加载器版本。", Themed("WarningBrush")));
            }
            if (!Directory.Exists(path))
            {
                stackPanel.Children.Add(CreateEmptyState("还没有这个文件夹，点击「打开文件夹」创建后再放入文件。", path, createFolder: true));
                ContentArea.Children.Add(stackPanel);
                return;
            }
            if (category2 == "worlds")
            {
                string[] directories = Directory.GetDirectories(path);
                Array.Sort(directories, (IComparer<string>?)StringComparer.OrdinalIgnoreCase);
                if (directories.Length == 0)
                {
                    stackPanel.Children.Add(CreateEmptyState("还没有存档，进入游戏创建世界后会出现在这里。", path, createFolder: false));
                }
                string[] array = directories;
                foreach (string dir in array)
                {
                    stackPanel.Children.Add(CreateFolderRow(dir));
                }
            }
            else
            {
                string[] array2 = (from f in Directory.GetFiles(path)
                    where IsContentFile(f) && MatchesCategory(f, category2)
                    select f).ToArray();
                if (category2 == "mods")
                {
                    Array.Sort(array2, (IComparer<string>?)StringComparer.OrdinalIgnoreCase);
                }
                else
                {
                    Array.Sort(array2, (string x, string y) => File.GetLastWriteTime(y).CompareTo(File.GetLastWriteTime(x)));
                }
                if (array2.Length == 0)
                {
                    stackPanel.Children.Add(CreateEmptyState((category2 == "logs") ? "还没有日志文件，启动一次游戏后就会有 latest.log。" : "这里是空的，点击「获取资源」从资源站下载，或点「打开文件夹」手动放入。", path, createFolder: false));
                }
                string[] array3 = array2;
                foreach (string text2 in array3)
                {
                    if (category2 == "mods" && Path.GetFileName(text2).ToLowerInvariant().EndsWith(".jar"))
                    {
                        stackPanel.Children.Add(CreateModItem(text2));
                    }
                    else if (category2 == "mods" && Path.GetFileName(text2).ToLowerInvariant().EndsWith(".jar.disabled"))
                    {
                        stackPanel.Children.Add(CreateModItem(text2));
                    }
                    else if (category2 == "screenshots")
                    {
                        stackPanel.Children.Add(CreateScreenshotRow(text2));
                    }
                    else
                    {
                        stackPanel.Children.Add(CreateContentRow(text2, category2));
                    }
                }
            }
            ContentArea.Children.Add(stackPanel);
        }
    
        private Border CreateNotice(string text, Brush tone)
        {
            byte a = 28;
            Color color = ((tone is SolidColorBrush solidColorBrush) ? solidColorBrush.Color : Color.FromRgb(56, 189, 248));
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B)),
                CornerRadius = new CornerRadius(12.0),
                Padding = new Thickness(14.0, 10.0, 14.0, 10.0),
                Margin = new Thickness(0.0, 0.0, 0.0, 10.0),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 12.0,
                    Foreground = Themed("TextSecondaryBrush"),
                    TextWrapping = TextWrapping.Wrap
                }
            };
        }
    
        private UIElement CreateEmptyState(string text, string path, bool createFolder)
        {
            string path2 = path;
            StackPanel stackPanel = new StackPanel
            {
                Margin = new Thickness(0.0, 30.0, 0.0, 20.0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stackPanel.Children.Add(new TextBlock
            {
                Text = "\ud83d\uddc2",
                FontSize = 34.0,
                HorizontalAlignment = HorizontalAlignment.Center,
                Opacity = 0.5
            });
            stackPanel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 13.0,
                Foreground = Themed("TextTertiaryBrush"),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0.0, 10.0, 0.0, 14.0),
                MaxWidth = 460.0
            });
            StackPanel stackPanel2 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stackPanel2.Children.Add(CreateActionButton("\ud83d\udcc2 打开文件夹", delegate
            {
                OpenFolder(path2);
            }));
            if (createFolder)
            {
                stackPanel2.Children.Add(CreateActionButton("➕ 创建文件夹", delegate
                {
                    OpenFolder(path2);
                    ShowContentTab(_contentCategory);
                }));
            }
            stackPanel.Children.Add(stackPanel2);
            return stackPanel;
        }
    
        private string FileMeta(string file)
        {
            try
            {
                FileInfo fileInfo = new FileInfo(file);
                return FormatFileSize(fileInfo.Length) + " · " + fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
                return "无法读取文件信息";
            }
        }
    
        private string FolderMeta(string dir)
        {
            try
            {
                DirectoryInfo directoryInfo = new DirectoryInfo(dir);
                return Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length + " 个文件 · " + directoryInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
            }
            catch
            {
                return "无法读取文件夹信息";
            }
        }
    
        private Button IconButton(string glyph, string tip, Brush background, RoutedEventHandler handler)
        {
            Button button = new Button
            {
                Content = glyph,
                Width = 34.0,
                Height = 34.0,
                Margin = new Thickness(6.0, 0.0, 0.0, 0.0),
                Background = background,
                Foreground = Themed("TextPrimaryBrush"),
                BorderThickness = new Thickness(0.0),
                FontSize = 14.0,
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = tip
            };
            button.Click += handler;
            return button;
        }
    
        private UIElement CreateContentRow(string file, string category)
        {
            string file2 = file;
            string fileName = Path.GetFileName(file2);
            bool flag = fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);
            bool flag2 = category == "resourcepacks" || category == "shaderpacks";
            string text = ((category == "logs") ? "\ud83d\udcc4" : ((category == "resourcepacks") ? "\ud83c\udfa8" : "✨"));
            Border border = new Border
            {
                Background = Themed("Surface3Brush"),
                CornerRadius = new CornerRadius(14.0),
                Padding = new Thickness(14.0, 10.0, 10.0, 10.0),
                Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
            };
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1.0, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            grid.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 20.0,
                Margin = new Thickness(0.0, 0.0, 12.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = (flag ? 0.4 : 1.0)
            });
            StackPanel stackPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            stackPanel.Children.Add(new TextBlock
            {
                Text = fileName,
                FontSize = 14.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = Themed("TextPrimaryBrush"),
                Opacity = (flag ? 0.55 : 1.0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stackPanel.Children.Add(new TextBlock
            {
                Text = (flag ? "⏸ 已禁用 · " : "") + FileMeta(file2),
                FontSize = 11.0,
                Foreground = Themed("TextTertiaryBrush"),
                Margin = new Thickness(0.0, 2.0, 0.0, 0.0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(stackPanel, 1);
            grid.Children.Add(stackPanel);
            StackPanel stackPanel2 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            if (flag2)
            {
                CheckBox checkBox = new CheckBox
                {
                    IsChecked = !flag,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0.0, 0.0, 10.0, 0.0),
                    ToolTip = "启用 / 禁用（只改名，不删除文件）"
                };
                string capturedToggle = file2;
                checkBox.Checked += delegate
                {
                    ToggleContent(capturedToggle, enable: true);
                };
                checkBox.Unchecked += delegate
                {
                    ToggleContent(capturedToggle, enable: false);
                };
                stackPanel2.Children.Add(checkBox);
            }
            stackPanel2.Children.Add(IconButton("\ud83d\udcc2", "打开", Themed("Surface4Brush"), delegate
            {
                OpenPath(file2);
            }));
            stackPanel2.Children.Add(IconButton("\ud83d\uddd1", "删除", Argb(30, 248, 113, 113), delegate
            {
                DeleteContentEntry(file2, isDirectory: false, "文件");
            }));
            Grid.SetColumn(stackPanel2, 2);
            grid.Children.Add(stackPanel2);
            border.Child = grid;
            return border;
        }
    
        private UIElement CreateFolderRow(string dir)
        {
            string dir2 = dir;
            string fileName = Path.GetFileName(dir2.TrimEnd(Path.DirectorySeparatorChar));
            Border border = new Border
            {
                Background = Themed("Surface3Brush"),
                CornerRadius = new CornerRadius(14.0),
                Padding = new Thickness(14.0, 10.0, 10.0, 10.0),
                Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
            };
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1.0, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            grid.Children.Add(new TextBlock
            {
                Text = "\ud83c\udf0d",
                FontSize = 20.0,
                Margin = new Thickness(0.0, 0.0, 12.0, 0.0),
                VerticalAlignment = VerticalAlignment.Center
            });
            StackPanel stackPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            stackPanel.Children.Add(new TextBlock
            {
                Text = fileName,
                FontSize = 14.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = Themed("TextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stackPanel.Children.Add(new TextBlock
            {
                Text = FolderMeta(dir2),
                FontSize = 11.0,
                Foreground = Themed("TextTertiaryBrush"),
                Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
            });
            Grid.SetColumn(stackPanel, 1);
            grid.Children.Add(stackPanel);
            StackPanel stackPanel2 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            stackPanel2.Children.Add(IconButton("\ud83d\udcc2", "在资源管理器中打开", Themed("Surface4Brush"), delegate
            {
                OpenFolder(dir2);
            }));
            stackPanel2.Children.Add(IconButton("\ud83d\uddd1", "删除存档（不可恢复）", Argb(30, 248, 113, 113), delegate
            {
                DeleteContentEntry(dir2, isDirectory: true, "存档");
            }));
            Grid.SetColumn(stackPanel2, 2);
            grid.Children.Add(stackPanel2);
            border.Child = grid;
            return border;
        }
    
        private UIElement CreateScreenshotRow(string file)
        {
            string file2 = file;
            string fileName = Path.GetFileName(file2);
            Border border = new Border
            {
                Background = Themed("Surface3Brush"),
                CornerRadius = new CornerRadius(14.0),
                Padding = new Thickness(10.0),
                Margin = new Thickness(0.0, 0.0, 0.0, 6.0),
                Cursor = Cursors.Hand,
                ToolTip = "点击用默认程序打开"
            };
            border.MouseLeftButtonUp += delegate
            {
                OpenPath(file2);
            };
            Grid grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1.0, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            Border border2 = new Border
            {
                Width = 96.0,
                Height = 54.0,
                CornerRadius = new CornerRadius(10.0),
                Background = Themed("Surface4Brush"),
                Margin = new Thickness(0.0, 0.0, 12.0, 0.0),
                ClipToBounds = true
            };
            try
            {
                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.UriSource = new Uri(file2);
                bitmapImage.DecodePixelWidth = 192;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                border2.Child = new Image
                {
                    Source = bitmapImage,
                    Stretch = Stretch.UniformToFill
                };
            }
            catch
            {
                border2.Child = new TextBlock
                {
                    Text = "\ud83d\uddbc",
                    FontSize = 20.0,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            grid.Children.Add(border2);
            StackPanel stackPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            stackPanel.Children.Add(new TextBlock
            {
                Text = fileName,
                FontSize = 13.0,
                FontWeight = FontWeights.SemiBold,
                Foreground = Themed("TextPrimaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            stackPanel.Children.Add(new TextBlock
            {
                Text = FileMeta(file2),
                FontSize = 11.0,
                Foreground = Themed("TextTertiaryBrush"),
                Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
            });
            Grid.SetColumn(stackPanel, 1);
            grid.Children.Add(stackPanel);
            StackPanel stackPanel2 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            stackPanel2.Children.Add(IconButton("\ud83d\uddd1", "删除截图", Argb(30, 248, 113, 113), delegate
            {
                DeleteContentEntry(file2, isDirectory: false, "截图");
            }));
            Grid.SetColumn(stackPanel2, 2);
            grid.Children.Add(stackPanel2);
            border.Child = grid;
            return border;
        }
    
        private void OpenPath(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    OpenFolder(Path.GetDirectoryName(path));
                    return;
                }
                Process.Start(new ProcessStartInfo(path)
                {
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                iOS26Dialog.Show("打开失败: " + ex.Message, "错误", DialogIcon.Error, DialogButtons.OK);
            }
        }
    
        private void ToggleContent(string path, bool enable)
        {
            try
            {
                string text = ((!enable) ? (path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? path : (path + ".disabled")) : (path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? path.Substring(0, path.Length - ".disabled".Length) : path));
                if (text != path && File.Exists(path))
                {
                    File.Move(path, text);
                }
            }
            catch (Exception ex)
            {
                iOS26Dialog.Show("操作失败: " + ex.Message, "错误", DialogIcon.Error, DialogButtons.OK);
            }
            ShowContentTab(_contentCategory);
        }
    
        private void DeleteContentEntry(string path, bool isDirectory, string label)
        {
            string fileName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
            if (iOS26Dialog.Show("确定要删除" + label + " \"" + fileName + "\" 吗？此操作不可恢复！", "删除" + label, DialogIcon.Warning, DialogButtons.YesNo) != true)
            {
                return;
            }
            try
            {
                if (isDirectory)
                {
                    Directory.Delete(path, recursive: true);
                }
                else
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                iOS26Dialog.Show("删除失败: " + ex.Message, "错误", DialogIcon.Error, DialogButtons.OK);
            }
            ShowContentTab(_contentCategory);
        }
    
        private bool InspectForModLoader()
        {
            if (_version.Id.Contains("Fabric", StringComparison.OrdinalIgnoreCase) || _version.Id.Contains("Forge", StringComparison.OrdinalIgnoreCase) || _version.InheritsFrom.Any((string i) => i.Contains("forge") || i.Contains("fabric")))
            {
                return true;
            }
            if (_version.Libraries.Any((Library l) => l.Name.Contains("minecraftforge") || l.Name.Contains("fabricmc")))
            {
                return true;
            }
            return false;
        }
    
        private void ShowSettingsTab()
        {
            ContentArea.Children.Clear();
            StackPanel stackPanel = new StackPanel();
            Border border = new Border
            {
                Background = Themed("InfoGlassBrush"),
                BorderBrush = Themed("GlassBorderBrush"),
                BorderThickness = new Thickness(1.0),
                CornerRadius = new CornerRadius(16.0),
                Padding = new Thickness(15.0, 10.0, 15.0, 10.0),
                Margin = new Thickness(0.0, 0.0, 0.0, 20.0)
            };
            border.Child = new TextBlock
            {
                Text = "ℹ\ufe0f 这些设置只对该游戏版本生效，不影响其他版本。",
                Foreground = Themed("InfoBrush"),
                FontSize = 13.0
            };
            stackPanel.Children.Add(border);
            List<UIElement> list = new List<UIElement>();
            list.Add(CreateSettingItem("版本隔离", CreateComboBox(new string[2] { "开启", "关闭" })));
            list.Add(CreateSettingItem("游戏窗口标题", CreateTextBox("跟随全局设置")));
            list.Add(CreateSettingItem("自定义信息", CreateTextBox("跟随全局设置")));
            int selectedIndex = 0;
            VersionSettings? settings = _settings;
            if (settings != null && settings.MemoryMode == MemoryAllocation.Custom)
            {
                selectedIndex = (string.IsNullOrEmpty(_settings.JavaPath) ? 1 : 2);
            }
            _javaCombo = CreateComboBox(new string[3] { "跟随全局设置", "智能匹配 (Auto)", "自定义..." }, selectedIndex);
            _javaCombo.SelectionChanged += JavaCombo_SelectionChanged;
            list.Add(CreateSettingItem("游戏 Java", _javaCombo));
            stackPanel.Children.Add(CreateSettingsGroup("启动选项", list.ToArray()));
            StackPanel stackPanel2 = new StackPanel();
            StackPanel stackPanel3 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
            };
            stackPanel3.Children.Add(CreateRadioButton("跟随全局设置", isChecked: true));
            stackPanel3.Children.Add(CreateRadioButton("自动配置", isChecked: false));
            stackPanel3.Children.Add(CreateRadioButton("自定义", isChecked: false));
            stackPanel2.Children.Add(CreateSettingItem("游戏内存", stackPanel3));
            stackPanel2.Children.Add(CreateSettingItem(" ", CreateRealMemorySlider()));
            stackPanel2.Children.Add(CreateSettingItem("启动游戏前进行内存优化", CreateComboBox(new string[3] { "跟随全局设置", "开启", "关闭" })));
            Grid grid = new Grid
            {
                Margin = new Thickness(0.0, 10.0, 0.0, 0.0)
            };
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            Grid grid2 = new Grid
            {
                Height = 4.0,
                Margin = new Thickness(0.0, 0.0, 0.0, 5.0)
            };
            grid2.Children.Add(new Border
            {
                Background = Themed("Surface4Brush"),
                CornerRadius = new CornerRadius(2.0)
            });
            grid2.Children.Add(new Border
            {
                Background = Themed("AccentBrush"),
                Width = 250.0,
                HorizontalAlignment = HorizontalAlignment.Left,
                CornerRadius = new CornerRadius(2.0)
            });
            Grid grid3 = new Grid();
            grid3.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1.0, GridUnitType.Star)
            });
            grid3.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            long totalSystemMemoryMB = LaunchService.GetTotalSystemMemoryMB();
            int num = _settings?.MaxMemoryMB ?? 4096;
            grid3.Children.Add(new TextBlock
            {
                Text = $"系统内存 {(double)totalSystemMemoryMB / 1024.0:F1} GB",
                Foreground = Utilities.ThemeBrush.TextSecondary,
                FontSize = 12.0
            });
            TextBlock element = new TextBlock
            {
                Text = $"游戏分配 {(double)num / 1024.0:F1} GB",
                Foreground = Utilities.ThemeBrush.TextSecondary,
                FontSize = 12.0
            };
            Grid.SetColumn(element, 1);
            grid3.Children.Add(element);
            grid.Children.Add(grid2);
            Grid.SetRow(grid3, 1);
            grid.Children.Add(grid3);
            stackPanel2.Children.Add(grid);
            UIElementCollection children = stackPanel.Children;
            UIElement[] items = new StackPanel[1] { stackPanel2 };
            children.Add(CreateSettingsGroup("游戏内存", items));
            List<UIElement> list2 = new List<UIElement>();
            _jvmArgsBox = CreateTextBox(string.IsNullOrEmpty(_customJvmArgs) ? "跟随全局设置" : _customJvmArgs, 60.0);
            _jvmArgsBox.TextChanged += delegate
            {
                if (base.IsLoaded)
                {
                    _customJvmArgs = _jvmArgsBox.Text;
                }
            };
            list2.Add(CreateSettingItem("Java 虚拟机参数", _jvmArgsBox));
            list2.Add(CreateSettingItem("游戏参数", CreateTextBox("跟随全局设置")));
            list2.Add(CreateSettingItem("启动前执行命令", CreateTextBox("")));
            StackPanel stackPanel4 = new StackPanel();
            stackPanel4.Children.Add(CreateCheckbox("禁止更新 Mod", isChecked: false));
            stackPanel4.Children.Add(CreateCheckbox("忽略 Java 兼容性警告", isChecked: false));
            stackPanel4.Children.Add(CreateCheckbox("关闭文件校验", isChecked: false));
            stackPanel4.Children.Add(CreateCheckbox("禁用 Java Launch Wrapper", isChecked: false));
            Grid grid4 = new Grid
            {
                Margin = new Thickness(120.0, 0.0, 0.0, 0.0)
            };
            grid4.Children.Add(stackPanel4);
            list2.Add(grid4);
            stackPanel.Children.Add(CreateSettingsGroup("高级选项", list2.ToArray()));
            Button button = new Button();
            button.Content = "➜ 全局设置";
            button.Padding = new Thickness(20.0, 8.0, 20.0, 8.0);
            button.HorizontalAlignment = HorizontalAlignment.Center;
            button.Margin = new Thickness(0.0, 20.0, 0.0, 20.0);
            button.Style = (Style)FindResource(typeof(Button));
            Button button2 = button;
            button2.Click += delegate
            {
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.NavigateTo("settings");
                }
                else if (base.NavigationService.CanGoBack)
                {
                    base.NavigationService.Navigate(new SettingsPage());
                }
            };
            stackPanel.Children.Add(button2);
            ContentArea.Children.Add(stackPanel);
        }
    
        // --- Overview tab ---
        /// <summary>
        /// 「概览」标签。内容宿主 <see cref="OverviewPanel"/> 在 XAML 里静态声明
        /// （颜色 / 圆角 / 描边全部 DynamicResource，主题切换实时生效），这里只刷新数据并挂回内容区。
        ///
        /// 注意：本方法<b>自己</b>把内容 Add 进 <see cref="ContentArea"/>。
        /// 旧实现在 TabButton_Click 里写的是 <c>SwitchToTab(() =&gt; CreateOverviewContent())</c>，
        /// 而 SwitchToTab 收的是 <see cref="Action"/>，返回值被直接丢掉 ——
        /// ContentArea 刚被 Clear 就再没有东西加回去，这就是「概览」永远空白的根因。
        /// </summary>
        private void ShowOverviewTab()
        {
            ContentArea.Children.Clear();
            ContentArea.Children.Add(CreateOverviewContent());
            TabOverview.IsChecked = true;
        }

        private UIElement CreateOverviewContent()
        {
            RefreshOverviewPanel();
            OverviewPanel.Visibility = Visibility.Visible;
            return OverviewPanel;
        }

        /// <summary>把版本 / Java / 内存 / 游玩统计灌进 XAML 里声明好的概览卡片。</summary>
        private void RefreshOverviewPanel()
        {
            var cfg = TsuruLauncher.Services.ConfigService.Instance.Settings;

            string gameDir = string.IsNullOrEmpty(_version.GameDir) ? _version.RootPath : _version.GameDir;
            bool isolated = !string.IsNullOrEmpty(_version.RootPath)
                && !string.Equals(_version.GameDir, _version.RootPath, StringComparison.OrdinalIgnoreCase);

            string customName = _settings?.CustomName ?? string.Empty;
            OverviewName.Text = string.IsNullOrEmpty(customName) ? _version.Id : customName;
            OverviewSubtitle.Text = "类型: " + DescribeVersionType(_version.Type)
                + " · 加载器: " + DetectLoaderName()
                + " · 需要 Java " + _version.RequiredJavaVersion + "+";
            OverviewIsolation.Text = isolated ? "📌 版本隔离" : "🌐 全局共享";

            OverviewInfoName.Text = _version.Id;
            OverviewInfoType.Text = DescribeVersionType(_version.Type);
            OverviewInfoLoader.Text = DetectLoaderName();
            OverviewInfoDir.Text = string.IsNullOrEmpty(gameDir) ? "未设置" : gameDir;
            OverviewInfoDir.ToolTip = gameDir;

            // Java：版本自己的设置优先，否则回落到全局
            string javaPath = _settings?.JavaPath ?? string.Empty;
            string javaSource = "版本设置";
            if (string.IsNullOrEmpty(javaPath))
            {
                javaPath = cfg.JavaPath;
                javaSource = "全局设置";
            }
            OverviewInfoJava.Text = string.IsNullOrEmpty(javaPath)
                ? "未检测到，启动时自动匹配"
                : javaPath + "（" + javaSource + "）";
            OverviewInfoJava.ToolTip = javaPath;

            // 内存：模式 + 上限 / 下限
            int maxMb = _settings?.MaxMemoryMB ?? cfg.MaxRam;
            int minMb = _settings?.MinMemoryMB ?? cfg.MinRam;
            OverviewInfoMemory.Text = (maxMb / 1024.0).ToString("F1") + " GB（最小 " + minMb + " MB · "
                + DescribeMemoryMode(_settings?.MemoryMode) + "）";

            OverviewLastPlayed.Text = DescribeLastPlayed(cfg.LastLaunchUtc);
            OverviewPlayTime.Text = cfg.TotalPlaySeconds >= 3600
                ? (cfg.TotalPlaySeconds / 3600.0).ToString("F1") + " h"
                : (cfg.TotalPlaySeconds / 60) + " min";
            OverviewLaunchCount.Text = cfg.LaunchCount + " 次";
            OverviewSystemMemory.Text = (GetSystemMemoryMb() / 1024.0).ToString("F1") + " GB";
        }

        private static long? _systemMemoryMbCache;

        /// <summary>WMI 查询有开销，缓存一次（概览 + 设置页都会用到）。</summary>
        private static long GetSystemMemoryMb()
        {
            if (_systemMemoryMbCache == null)
            {
                try { _systemMemoryMbCache = TsuruLauncher.Services.LaunchService.GetTotalSystemMemoryMB(); }
                catch { _systemMemoryMbCache = 8192; }
            }
            return _systemMemoryMbCache.Value;
        }

        private static string DescribeVersionType(string? type)
        {
            string value = (type ?? string.Empty).Trim();
            switch (value.ToLowerInvariant())
            {
                case "release": return "正式版 (release)";
                case "snapshot": return "快照版 (snapshot)";
                case "old_beta": return "远古 Beta";
                case "old_alpha": return "远古 Alpha";
                default: return string.IsNullOrEmpty(value) ? "未知" : value;
            }
        }

        private static string DescribeMemoryMode(MemoryAllocation? mode)
        {
            switch (mode)
            {
                case MemoryAllocation.Auto: return "自动配置";
                case MemoryAllocation.Custom: return "自定义";
                default: return "跟随全局设置";
            }
        }

        /// <summary>从版本 id / inheritsFrom / libraries 里判断加载器。</summary>
        private string DetectLoaderName()
        {
            var haystack = new List<string> { _version.Id ?? string.Empty };
            if (_version.InheritsFrom != null) haystack.AddRange(_version.InheritsFrom);
            if (_version.Libraries != null)
                foreach (var library in _version.Libraries) haystack.Add(library?.Name ?? string.Empty);

            string joined = string.Join(" | ", haystack).ToLowerInvariant();
            if (joined.Contains("neoforge")) return "NeoForge";
            if (joined.Contains("forge")) return "Forge";
            if (joined.Contains("fabric")) return "Fabric";
            if (joined.Contains("quilt")) return "Quilt";
            if (joined.Contains("optifine")) return "OptiFine";
            if (joined.Contains("liteloader")) return "LiteLoader";
            return "原版 (Vanilla)";
        }

        private static string DescribeLastPlayed(string? lastLaunchUtc)
        {
            if (string.IsNullOrEmpty(lastLaunchUtc)) return "从未启动";
            if (!DateTime.TryParse(lastLaunchUtc, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                    out DateTime utc))
                return lastLaunchUtc;

            DateTime local = utc.ToLocalTime();
            double days = (DateTime.Now - local).TotalDays;
            string relative = days < 1 ? "今天"
                : days < 2 ? "昨天"
                : days < 30 ? ((int)days) + " 天前"
                : local.ToString("yyyy-MM-dd");
            return local.ToString("yyyy-MM-dd HH:mm") + "（" + relative + "）";
        }

        private void OverviewLaunch_Click(object sender, RoutedEventArgs e) => LaunchSelectedVersion();

        private void OverviewOpenDir_Click(object sender, RoutedEventArgs e) => OpenFolder(_version.GameDir);

    private string GetModsPath()
    {
        return Path.Combine(_version.GameDir, "mods");
    }

    private CheckBox CreateCheckbox(string text, bool isChecked, bool isEnabled = true)
    {
        return new CheckBox
        {
            Content = text,
            IsChecked = isChecked,
            IsEnabled = isEnabled,
            Foreground = Themed("TextPrimaryBrush"),
            Margin = new Thickness(0.0, 0.0, 0.0, 10.0)
        };
    }

    private RadioButton CreateRadioButton(string text, bool isChecked)
    {
        return new RadioButton
        {
            Content = text,
            IsChecked = isChecked,
            Foreground = Themed("TextPrimaryBrush"),
            Margin = new Thickness(0.0, 0.0, 15.0, 0.0)
        };
    }

    private TextBox CreateTextBox(string text, double height = 35.0)
    {
        return new TextBox
        {
            Text = text,
            Height = height,
            Padding = new Thickness(5.0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    private ComboBox CreateComboBox(IEnumerable<string> items, int selectedIndex = 0)
    {
        return new ComboBox
        {
            ItemsSource = items,
            SelectedIndex = selectedIndex,
            Height = 35.0,
            VerticalContentAlignment = VerticalAlignment.Center
        };
    }

    private UIElement CreateRealMemorySlider()
    {
        StackPanel stackPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal
        };
        Slider slider = new Slider
        {
            Minimum = 1024.0,
            Maximum = 16384.0,
            Value = (_settings?.MaxMemoryMB ?? 4096),
            Width = 300.0,
            VerticalAlignment = VerticalAlignment.Center,
            IsSnapToTickEnabled = true,
            TickFrequency = 512.0
        };
        TextBlock label = new TextBlock
        {
            Text = $"{slider.Value} MB",
            Margin = new Thickness(15.0, 0.0, 0.0, 0.0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Themed("TextPrimaryBrush"),
            MinWidth = 60.0
        };
        slider.ValueChanged += delegate(object s, RoutedPropertyChangedEventArgs<double> e)
        {
            label.Text = $"{e.NewValue} MB";
            if (_settings != null)
            {
                _settings.MaxMemoryMB = (int)e.NewValue;
            }
        };
        stackPanel.Children.Add(slider);
        stackPanel.Children.Add(label);
        return stackPanel;
    }

    private Border CreateSettingsGroup(string title, UIElement[] items)
    {
        Border border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(20, byte.MaxValue, byte.MaxValue, byte.MaxValue)),
            CornerRadius = new CornerRadius(24.0),
            Padding = new Thickness(20.0),
            Margin = new Thickness(0.0, 0.0, 0.0, 20.0)
        };
        StackPanel stackPanel = new StackPanel();
        stackPanel.Children.Add(new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.Bold,
            Foreground = Themed("TextPrimaryBrush"),
            Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
        });
        foreach (UIElement element in items)
        {
            stackPanel.Children.Add(element);
        }
        border.Child = stackPanel;
        return border;
    }

    private Grid CreateSettingItem(string label, UIElement control)
    {
        Grid grid = new Grid
        {
            Margin = new Thickness(0.0, 0.0, 0.0, 15.0)
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(120.0)
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1.0, GridUnitType.Star)
        });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Themed("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private TextBlock CreateSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 16.0,
            FontWeight = FontWeights.SemiBold,
            Foreground = Themed("TextPrimaryBrush"),
            Margin = new Thickness(0.0, 10.0, 0.0, 15.0)
        };
    }

    private UIElement CreateModItem(string modPath)
    {
        string fileName = Path.GetFileName(modPath);
        bool flag = modPath.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
        string realJar = (flag ? modPath : modPath.Substring(0, modPath.Length - ".disabled".Length));
        ModJarInfo modJarInfo = ModJarInspector.Inspect(realJar, !flag);
        string text = (string.Equals(_version.GameDir, _version.RootPath, StringComparison.OrdinalIgnoreCase) ? "\ud83c\udf10 全局共享" : "\ud83d\udccc 仅此版本");
        Border border = new Border
        {
            Background = Themed(flag ? "Surface4Brush" : "Surface2Brush"),
            CornerRadius = new CornerRadius(20.0),
            Padding = new Thickness(15.0, 10.0, 10.0, 10.0),
            Margin = new Thickness(0.0, 0.0, 0.0, 6.0)
        };
        Grid grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1.0, GridUnitType.Star)
        });
        grid.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto
        });
        CheckBox checkBox = new CheckBox
        {
            IsChecked = flag,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0.0, 0.0, 12.0, 0.0)
        };
        string capturedPath = modPath;
        checkBox.Checked += delegate
        {
            ToggleMod(capturedPath, enable: true);
        };
        checkBox.Unchecked += delegate
        {
            ToggleMod(capturedPath, enable: false);
        };
        grid.Children.Add(checkBox);
        StackPanel stackPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center
        };
        stackPanel.Children.Add(new TextBlock
        {
            Text = modJarInfo.DisplayNameOrFile,
            FontSize = 14.0,
            FontWeight = FontWeights.SemiBold,
            Foreground = Themed("TextPrimaryBrush"),
            Opacity = (flag ? 1.0 : 0.5),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        string text2 = modJarInfo.LoaderIcon + " " + modJarInfo.DetailText;
        if (!flag)
        {
            text2 = "⏸ 已禁用 · " + text2;
        }
        stackPanel.Children.Add(new TextBlock
        {
            Text = text2 + " · " + text,
            FontSize = 11.0,
            Foreground = Themed("TextTertiaryBrush"),
            Margin = new Thickness(0.0, 2.0, 0.0, 0.0),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(stackPanel, 1);
        grid.Children.Add(stackPanel);
        Button button = new Button
        {
            Content = "\ud83d\uddd1",
            Width = 34.0,
            Height = 34.0,
            Margin = new Thickness(8.0, 0.0, 0.0, 0.0),
            Background = new SolidColorBrush(Color.FromArgb(30, byte.MaxValue, 82, 82)),
            Foreground = Themed("DangerBrush"),
            BorderThickness = new Thickness(0.0),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "删除此 Mod"
        };
        button.Click += delegate
        {
            DeleteMod(realJar, fileName);
        };
        Grid.SetColumn(button, 2);
        grid.Children.Add(button);
        border.Child = grid;
        return border;
    }

    private void DeleteMod(string jarPath, string fileName)
    {
        if (iOS26Dialog.Show("确定要删除 Mod \"" + fileName + "\" 吗？此操作不可恢复！", "删除 Mod", DialogIcon.Warning, DialogButtons.YesNo) != true)
        {
            return;
        }
        try
        {
            if (File.Exists(jarPath))
            {
                File.Delete(jarPath);
            }
            string path = jarPath + ".disabled";
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            ShowModsTab();
        }
        catch (Exception ex)
        {
            iOS26Dialog.Show("删除失败: " + ex.Message, "错误", DialogIcon.Error, DialogButtons.OK);
        }
    }

    private void ToggleMod(string modPath, bool enable)
    {
        try
        {
            string text = ((!enable) ? (modPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? modPath : (modPath + ".disabled")) : (modPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? modPath.Substring(0, modPath.Length - ".disabled".Length) : modPath));
            if (text != modPath && File.Exists(modPath))
            {
                File.Move(modPath, text);
                ShowModsTab();
            }
        }
        catch
        {
        }
    }

    private string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
        {
            return bytes + " B";
        }
        if (bytes < 1048576)
        {
            return ((double)bytes / 1024.0).ToString("F1") + " KB";
        }
        return ((double)bytes / 1024.0 / 1024.0).ToString("F1") + " MB";
    }

    private Button CreateActionButton(string text, RoutedEventHandler clickHandler, string? bgColor = null)
    {
        Button button = new Button
        {
            Content = text,
            Height = 40.0,
            Padding = new Thickness(20.0, 0.0, 20.0, 0.0),
            Margin = new Thickness(0.0, 0.0, 10.0, 10.0),
            Background = (string.IsNullOrEmpty(bgColor) ? Themed("Surface4Brush") : new SolidColorBrush((Color)ColorConverter.ConvertFromString(bgColor))),
            Foreground = Themed("TextPrimaryBrush"),
            BorderThickness = new Thickness(0.0),
            FontSize = 14.0,
            Cursor = Cursors.Hand
        };
        button.Click += clickHandler;
        return button;
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start("explorer.exe", path);
        }
        catch
        {
        }
    }

    private void JavaCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settings == null || _javaCombo == null || !base.IsLoaded)
        {
            return;
        }
        if (_javaCombo.SelectedIndex == 2)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Filter = "Java Executable (javaw.exe;java.exe)|javaw.exe;java.exe|All Files (*.*)|*.*",
                Title = "选择 Java 可执行文件"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                _settings.JavaPath = openFileDialog.FileName;
                _settings.MemoryMode = MemoryAllocation.Custom;
            }
            else
            {
                _javaCombo.SelectedIndex = 0;
            }
        }
        else if (_javaCombo.SelectedIndex == 1)
        {
            _settings.JavaPath = string.Empty;
            _settings.MemoryMode = MemoryAllocation.Custom;
        }
        else
        {
            _settings.JavaPath = string.Empty;
            _settings.MemoryMode = MemoryAllocation.FollowGlobal;
        }
    }

    private void LaunchSelectedVersion()
    {
        SaveVersionSettings();
        if (Application.Current.MainWindow is MainWindow { DataContext: MainViewModel dataContext })
        {
            GameInstance gameInstance = dataContext.GameVersions.FirstOrDefault((GameInstance v) => v.Id == _version.Id);
            if (gameInstance != null)
            {
                dataContext.SelectedVersion = gameInstance;
                if (dataContext.LaunchGameCommand.CanExecute(null))
                {
                    dataContext.LaunchGameCommand.Execute(null);
                }
                return;
            }
        }
        iOS26Dialog.Show("未找到对应版本，请返回主页选择版本", "提示", DialogIcon.Warning, DialogButtons.OK);
    }

    private async Task DeleteVersionAsync()
    {
        if (iOS26Dialog.Show("确定要删除版本 \"" + _version.Id + "\" 吗？此操作不可恢复！", "删除版本", DialogIcon.Warning, DialogButtons.YesNo) != true)
        {
            return;
        }
        try
        {
            await Task.Run(delegate
            {
                string path = Path.Combine(_version.RootPath, "versions", _version.Id);
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            });
            iOS26Dialog.Show("版本已删除", "成功", DialogIcon.Info, DialogButtons.OK);
            Window mainWindow = Application.Current.MainWindow;
            MainViewModel vm = default(MainViewModel);
            int num;
            if (mainWindow is MainWindow { DataContext: var dataContext })
            {
                vm = dataContext as MainViewModel;
                num = ((vm != null) ? 1 : 0);
            }
            else
            {
                num = 0;
            }
            if (num != 0)
            {
                await vm.LoadVersionsAsync();
            }
            if (base.NavigationService.CanGoBack)
            {
                base.NavigationService.GoBack();
            }
        }
        catch (Exception ex)
        {
            iOS26Dialog.Show("删除失败: " + ex.Message, "错误", DialogIcon.Error, DialogButtons.OK);
        }
    }

    private string GetString(string key)
    {
        if (FindResource(key) is string result)
        {
            return result;
        }
        return "[" + key + "]";
    }

    }
}
