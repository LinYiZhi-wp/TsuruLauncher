using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TsuruLauncher.Models;
using TsuruLauncher.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TsuruLauncher.Controls;

namespace TsuruLauncher.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly GameService _gameService;
        private readonly LaunchService _launchService;
        private readonly ConfigService _configService;

        public ConfigService ConfigService => _configService;
        public NotificationService NotificationService { get; }
        public TsuruLauncher.Services.Network.DownloadManagerService DownloadManager => TsuruLauncher.Services.Network.DownloadManagerService.Instance;

        public event Action<int, string>? PreloadProgressChanged;
        public event Action? PreloadCompleted;

        [ObservableProperty]
        private string _title = "Tsuru Launcher";

        [ObservableProperty]
        private AccountManager _accountManager;

        public ObservableCollection<GameInstance> GameVersions { get; } = new ObservableCollection<GameInstance>();

        [ObservableProperty]
        private GameInstance? _selectedVersion;

        [ObservableProperty]
        private string _statusMessage = "Ready to Launch";

        [ObservableProperty]
        private bool _isLaunching = false;

        [ObservableProperty]
        private ImageSource? _backgroundImage;

        [ObservableProperty]
        private double _backgroundOpacity = 0.6;

        [ObservableProperty]
        private double _blurEffectRadius = 0;

        [ObservableProperty]
        private bool _isGlobalResourcesOverlayActive = false;

        [ObservableProperty]
        private bool _isLoadingBackground = false;

        [ObservableProperty]
        private bool _isPreloading = true;

        // ---- Dashboard (home) data ----
        [ObservableProperty]
        private string _javaVersionText = "检测中...";

        [ObservableProperty]
        private string _lastPlayedText = "从未";

        [ObservableProperty]
        private string _playTimeText = "0 min";

        [ObservableProperty]
        private string _launchCountText = "0";

        [ObservableProperty]
        private string _versionCountText = "0";

        [ObservableProperty]
        private string _modCountText = "0";

        [ObservableProperty]
        private string _saveCountText = "0";

        [ObservableProperty]
        private string _gameDirText = "";

        /// <summary>首页问候语（按当前时间自动变化）。</summary>
        public string GreetingText
        {
            get
            {
                int hour = System.DateTime.Now.Hour;
                string part = hour < 6 ? "凌晨好" : hour < 12 ? "早上好" : hour < 14 ? "中午好" : hour < 19 ? "下午好" : "晚上好";
                string name = string.IsNullOrWhiteSpace(PlayerName) ? "玩家" : PlayerName;
                return part + "，" + name;
            }
        }

        /// <summary>首页「最近世界」卡片数据。</summary>
        public System.Collections.ObjectModel.ObservableCollection<Models.WorldEntry> RecentWorlds { get; } = new();

        [ObservableProperty]
        private bool _hasRecentWorlds;

        // ─────────── 首页小组件（Axolotl 式网格） ───────────
        public System.Collections.ObjectModel.ObservableCollection<Models.HomeWidget> HomeWidgets { get; } = new();

        /// <summary>true = 极简主页，false = 信息主页。</summary>
        [ObservableProperty]
        private bool _isMinimalHome = true;

        [ObservableProperty]
        private bool _hasVersions;

        /// <summary>「9月19日 星期六」。</summary>
        [ObservableProperty]
        private string _todayText = "";

        /// <summary>今天是否玩过（信息主页的日历小件用）。</summary>
        [ObservableProperty]
        private string _todayPlayText = "未游玩";

        public string HomeModeLabel => IsMinimalHome ? "信息主页" : "极简主页";

        partial void OnIsMinimalHomeChanged(bool value)
        {
            OnPropertyChanged(nameof(HomeModeLabel));
            _configService.Settings.HomeMinimal = value;
            _configService.SaveConfigDebounced();

            // 添加 / 编辑小组件只对**网格视图（信息主页）**有意义 —— 切到极简主页就退出编辑态。
            if (value) IsHomeEditing = false;
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void ToggleHomeMode() => IsMinimalHome = !IsMinimalHome;

        // ─────────── 账号下拉浮层（Axolotl 形态：首页账号卡点开的那个浮层） ───────────
        //
        // 以前首页账号卡的右键菜单是一个在代码里现搭的 Popup（临时 Border + StackPanel），
        // 没有进入/退出过渡，也没有「点外部关闭」。现在改成页面内的浮层：
        //   * 展开 / 收起由 PageTransition.PlayPopup 播
        //     （展开 250ms cubic-bezier(0.15, 1.4, 0.64, 0.96)，scale 0.96 -> 1 + translateY 4px + opacity；
        //      收起 150ms ease），
        //   * 内容是账号列表（当前账号 + 正版/离线/外置 标签 + 复制 / 删除）与三行「+ 添加 …」，
        //   * 全部动作都落到 AccountManager（Accounts / CurrentAccount / LoginMicrosoft /
        //     LoginYggdrasil / LoginOffline / RemoveAccount）。

        /// <summary>账号下拉浮层是否展开（HomePage 监听它播开合动画）。</summary>
        [ObservableProperty]
        private bool _isAccountFlyoutOpen;

        public void ToggleAccountFlyout() => IsAccountFlyoutOpen = !IsAccountFlyoutOpen;

        public void CloseAccountFlyout()
        {
            if (IsAccountFlyoutOpen) IsAccountFlyoutOpen = false;
        }

        /// <summary>点浮层里的账号行 = 切换当前账号（写配置走合并写盘，不在点击路径上落盘）。</summary>
        public void SelectAccount(Account? account)
        {
            if (account == null) return;
            if (ReferenceEquals(account, AccountManager.CurrentAccount)) return;

            AccountManager.CurrentAccount = account;
            PersistSelectedAccount();
        }

        /// <summary>删除账号（AccountManager.RemoveAccount 会自动把 CurrentAccount 落到下一个账号）。</summary>
        public void RemoveAccount(Account? account)
        {
            if (account == null) return;
            AccountManager.RemoveAccount(account);
            PersistSelectedAccount();
        }

        /// <summary>微软正版登录（走 AuthenticationService 的登录窗口）。</summary>
        public async Task LoginMicrosoftAsync()
        {
            await AccountManager.LoginMicrosoft();
            PersistSelectedAccount();
        }

        /// <summary>外置登录（Yggdrasil / authlib-injector）。</summary>
        public async Task LoginYggdrasilAsync(string server, string username, string password)
        {
            await AccountManager.LoginYggdrasil(server, username, password);
            PersistSelectedAccount();
        }

        /// <summary>离线账号：只用名字。</summary>
        public void LoginOffline(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return;
            AccountManager.LoginOffline(username.Trim());
            PersistSelectedAccount();
        }

        /// <summary>把当前账号落进配置（合并写盘）+ 刷新玩家卡 / 账号行的「当前」标记。</summary>
        /// <summary>
        /// 账号的唯一标识：正版/外置用 Uuid；没有 Uuid 的退化成「用户名|类型」。
        /// </summary>
        private static string? AccountKey(Account? a)
            => a == null ? null
               : !string.IsNullOrWhiteSpace(a.Uuid) ? a.Uuid
               : $"{a.Username}|{a.Type}";

        /// <summary>
        /// 把**整个账号列表** + 当前账号标识写进配置。
        /// ⚠ 不要再只存 CurrentAccount —— 那样切换账号会把上一个覆盖掉。
        /// </summary>
        private void PersistAccounts()
        {
            var cfg = _configService.Settings;
            cfg.Accounts = AccountManager.Accounts.ToList();
            cfg.CurrentAccountKey = AccountKey(AccountManager.CurrentAccount);
            cfg.SelectedAccount = null;   // 新格式不再用这个字段
            _configService.SaveConfigDebounced();
        }

        // ── 账号导出 / 导入 ────────────────────────────────────────────
        // 账号存在本地 %APPDATA%，**打包发布 / 更新启动器都不会动它**，
        // 所以开发者自己打包后不需要重新登录。
        // 但换电脑时本地文件不会跟着走 —— 这两个方法就是为此准备的：
        // 导出一个 json，拷到另一台机器导入即可。

        /// <summary>导出全部账号到一个 json 文件（**含令牌**，要提醒用户妥善保管）。</summary>
        public bool ExportAccounts(string path)
        {
            try
            {
                var payload = new
                {
                    version = 1,
                    exportedAt = DateTime.Now.ToString("o"),
                    accounts = AccountManager.Accounts.ToList(),
                    currentKey = AccountKey(AccountManager.CurrentAccount),
                };

                string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload,
                    Newtonsoft.Json.Formatting.Indented);
                System.IO.File.WriteAllText(path, json, System.Text.Encoding.UTF8);
                return true;
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "导出账号");
                return false;
            }
        }

        /// <summary>
        /// 从 json 导入账号。**按 Uuid 合并**（已存在的跳过），不会清掉现有账号。
        /// 返回 (新增数, 跳过数)。
        /// </summary>
        public (int added, int skipped) ImportAccounts(string path)
        {
            int added = 0, skipped = 0;
            try
            {
                string json = System.IO.File.ReadAllText(path, System.Text.Encoding.UTF8);
                var root = Newtonsoft.Json.Linq.JObject.Parse(json);

                // 兼容两种格式：{accounts:[...]} 或者直接是数组
                var arr = root["accounts"] as Newtonsoft.Json.Linq.JArray
                          ?? Newtonsoft.Json.Linq.JArray.Parse(json);

                foreach (var tok in arr)
                {
                    var acc = tok.ToObject<Account>();
                    if (acc == null || string.IsNullOrWhiteSpace(acc.Username)) { skipped++; continue; }

                    // 按 key 判重（Uuid 或 用户名|类型）
                    string key = AccountKey(acc) ?? "";
                    bool exists = AccountManager.Accounts.Any(a => AccountKey(a) == key);
                    if (exists) { skipped++; continue; }

                    AccountManager.AddAccount(acc);
                    added++;
                }

                if (added > 0) PersistAccounts();
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "导入账号");
            }
            return (added, skipped);
        }

        private void PersistSelectedAccount()
        {
            PersistAccounts();
            RefreshPlayerCard();
            RefreshCurrentAccountFlags();
            OnPropertyChanged(nameof(CurrentAccount));
            OnPropertyChanged(nameof(Accounts));
        }

        /// <summary>当前账号（浮层 / 卡片直接绑定）。</summary>
        public Account? CurrentAccount => AccountManager.CurrentAccount;

        /// <summary>账号列表（等价于 AccountManager.Accounts，浮层绑定它）。</summary>
        public System.Collections.ObjectModel.ObservableCollection<Account> Accounts => AccountManager.Accounts;

        /// <summary>重建每个账号行上的「当前」标记。</summary>
        public void RefreshCurrentAccountFlags()
        {
            var current = AccountManager.CurrentAccount;
            foreach (var acc in AccountManager.Accounts)
            {
                acc.IsCurrent = ReferenceEquals(acc, current);
            }
        }

        [ObservableProperty]
        private bool _isHomeEditing;

        [ObservableProperty]
        private bool _isHomeCompact;

        [ObservableProperty]
        private bool _isWidgetPickerOpen;

        /// <summary>布局模式：false = grid（自动装箱，Axolotl 'grid'）/ true = free（自由摆放，'free'）。</summary>
        [ObservableProperty]
        private bool _isFreeLayout;

        partial void OnIsFreeLayoutChanged(bool value)
        {
            Utilities.Logger.LogInfo("[WidgetEdit] layout=" + (value ? "free" : "grid"));
            _configService.Settings.HomeWidgetFreeLayout = value;
            _configService.SaveConfigDebounced();
        }

        // ─────────── 撤销栈（Axolotl 底部工具条的撤销按钮）───────────
        private readonly System.Collections.Generic.List<string> _layoutHistory = new();

        /// <summary>任何会改布局的操作**之前**先压一份快照。</summary>
        private void PushLayoutHistory()
        {
            _layoutHistory.Add(SerializeLayout());
            if (_layoutHistory.Count > 30) _layoutHistory.RemoveAt(0);
        }

        public bool CanUndoLayout => _layoutHistory.Count > 0;

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void UndoLayout()
        {
            if (_layoutHistory.Count == 0) return;
            string snap = _layoutHistory[_layoutHistory.Count - 1];
            _layoutHistory.RemoveAt(_layoutHistory.Count - 1);
            RestoreLayout(snap);
            Utilities.Logger.LogInfo("[WidgetEdit] undo -> " + snap);
        }

        private string SerializeLayout()
            => string.Join(";", HomeWidgets.Select(w => w.Kind + ":" + w.SizeKey + "@" + w.X + "," + w.Y));

        private void RestoreLayout(string snap)
        {
            HomeWidgets.Clear();
            foreach (var part in snap.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string body = part;
                int x = 0, y = 0;
                int at = body.IndexOf('@');
                if (at >= 0)
                {
                    var xy = body.Substring(at + 1).Split(',');
                    if (xy.Length == 2) { int.TryParse(xy[0], out x); int.TryParse(xy[1], out y); }
                    body = body.Substring(0, at);
                }
                var bits = body.Split(':');
                if (bits.Length != 2) continue;
                HomeWidgets.Add(new Models.HomeWidget { Kind = bits[0], SizeKey = bits[1], X = x, Y = y });
            }
            NotifyWidgetSlots();
            SaveWidgetLayout();
        }

        public string HomeLayoutHint => IsHomeEditing ? "完成" : "自定义";

        partial void OnIsHomeEditingChanged(bool value)
        {
            OnPropertyChanged(nameof(HomeLayoutHint));
            // ⚠ 退出编辑态必须顺手把「添加小组件」收掉。
            //   原来这句只在 ToggleHomeEdit() 里，而 MainWindow.HomeEdit_Click 是**直接写**
            //   IsHomeEditing 的（走 ToggleButton 的 IsChecked），根本不经过 ToggleHomeEdit ——
            //   所以退出编辑态后添加面板一直挂在上面关不掉（用户实测报的就是这个）。
            //   放进 OnChanged 里，无论从哪条路进来都保证成对。
            if (!value) IsWidgetPickerOpen = false;
            Utilities.Logger.LogInfo("[WidgetEdit] editing=" + value + " picker=" + IsWidgetPickerOpen +
                " widgets=" + HomeWidgets.Count);
        }

        /// <summary>「可添加」= AvailableWidgetKinds 里还没启用的那些（右栏编辑器用）。</summary>
        public System.Collections.Generic.List<Models.HomeWidget> AddableWidgetKinds =>
            AvailableWidgetKinds.Where(k => !HasWidget(k.Kind)).ToList();

        /// <summary>已启用的小组件（右栏编辑器列出来给改尺寸 / 移除）。</summary>
        public System.Collections.Generic.IEnumerable<Models.HomeWidget> ActiveWidgets => HomeWidgets;

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void ToggleHomeEdit()
        {
            IsHomeEditing = !IsHomeEditing;
            if (!IsHomeEditing) IsWidgetPickerOpen = false;
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void ToggleWidgetPicker() => IsWidgetPickerOpen = !IsWidgetPickerOpen;

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void AddWidget(string? kind)
        {
            if (string.IsNullOrEmpty(kind)) return;
            if (HomeWidgets.Any(w => w.Kind == kind)) return;
            PushLayoutHistory();
            HomeWidgets.Add(new Models.HomeWidget { Kind = kind, SizeKey = DefaultSizeFor(kind) });
            Utilities.Logger.LogInfo("[WidgetEdit] add kind=" + kind + " -> " + HomeWidgets.Count + " 个");
            NotifyWidgetSlots();
            SaveWidgetLayout();
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void RemoveWidget(Models.HomeWidget? widget)
        {
            if (widget == null || HomeWidgets.Count <= 1) return;
            PushLayoutHistory();
            HomeWidgets.Remove(widget);
            Utilities.Logger.LogInfo("[WidgetEdit] remove kind=" + widget.Kind + " -> " + HomeWidgets.Count + " 个");
            NotifyWidgetSlots();
            SaveWidgetLayout();
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void CycleWidgetSize(Models.HomeWidget? widget)
        {
            if (widget == null) return;
            string[] order = { "1x1", "2x1", "1x2", "2x2", "3x1", "3x2" };
            int index = System.Array.IndexOf(order, widget.SizeKey);
            PushLayoutHistory();
            widget.SizeKey = order[(index + 1) % order.Length];
            Utilities.Logger.LogInfo("[WidgetEdit] resize kind=" + widget.Kind + " -> " + widget.SizeKey);
            NotifyWidgetSlots();
            SaveWidgetLayout();
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void ResetWidgets()
        {
            BuildDefaultWidgets();
            Utilities.Logger.LogInfo("[WidgetEdit] reset -> " + HomeWidgets.Count + " 个");
            NotifyWidgetSlots();
            SaveWidgetLayout();
        }

        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void ToggleCompact()
        {
            IsHomeCompact = !IsHomeCompact;
            Models.HomeWidget.CompactMode = IsHomeCompact;
            foreach (var widget in HomeWidgets) widget.RefreshSize();
            _configService.Settings.HomeCompact = IsHomeCompact;
            _configService.SaveConfigDebounced();
        }

        // ─────────── 小组件编辑：把「按种类取实例」暴露成属性，XAML 才能把
        //   CycleWidgetSize / RemoveWidget 的 CommandParameter 绑到对应的 HomeWidget 上 ───────────
        //
        // 仪表盘的 5 张卡是写死的 XAML，各自对应一个 Kind。卡片本身通过
        // {Binding WidgetXxx} 拿到实例：为 null 就说明这个小组件被移除了，卡片整张隐藏。

        public Models.HomeWidget? WidgetInstance => FindWidget("instance");
        public Models.HomeWidget? WidgetStats => FindWidget("stats");
        public Models.HomeWidget? WidgetWorlds => FindWidget("worlds");
        public Models.HomeWidget? WidgetAccount => FindWidget("account");
        public Models.HomeWidget? WidgetNews => FindWidget("news");

        private Models.HomeWidget? FindWidget(string kind)
            => HomeWidgets.FirstOrDefault(w => string.Equals(w.Kind, kind, StringComparison.Ordinal));

        /// <summary>HomeWidgets 变化后刷新上面那批槽位属性（否则卡片不会跟着增删显隐）。</summary>
        private void NotifyWidgetSlots()
        {
            OnPropertyChanged(nameof(WidgetInstance));
            OnPropertyChanged(nameof(WidgetStats));
            OnPropertyChanged(nameof(WidgetWorlds));
            OnPropertyChanged(nameof(WidgetAccount));
            OnPropertyChanged(nameof(WidgetNews));
            OnPropertyChanged(nameof(WidgetCountText));
            OnPropertyChanged(nameof(AddableWidgetKinds));
            OnPropertyChanged(nameof(ActiveWidgets));
        }

        public string WidgetCountText => $"已启用 {HomeWidgets.Count} 个";

        /// <summary>
        /// 可选小组件种类（供「添加小组件」面板列出）。
        /// 用 <see cref="Models.HomeWidget"/> 本身当条目 —— 它的 Title / Glyph 是按 Kind 算出来的，
        /// 直接可绑（用 ValueTuple 的话 XAML 只能绑 Item1/Item2/Item3，不可读）。
        /// </summary>
        /// 现在每个 Kind 都有对应的 DataTemplate（TplInstance / TplStats / … / TplShortcuts），
        /// 所以全部可添加。之前 java / shortcuts / greeting 没有卡，点了什么都不出现。
        public Models.HomeWidget[] AvailableWidgetKinds { get; } =
        {
            new Models.HomeWidget { Kind = "instance" },
            new Models.HomeWidget { Kind = "stats" },
            new Models.HomeWidget { Kind = "worlds" },
            new Models.HomeWidget { Kind = "account" },
            new Models.HomeWidget { Kind = "news" },
            new Models.HomeWidget { Kind = "java" },
            new Models.HomeWidget { Kind = "shortcuts" },
            new Models.HomeWidget { Kind = "greeting" }
        };

        /// <summary>free 模式：把某个小组件放到指定格子（拖放落点）。</summary>
        public void PlaceWidgetAt(Models.HomeWidget? widget, int col, int row)
        {
            if (widget == null) return;
            int cols = Models.HomeWidget.ColumnCount;
            PushLayoutHistory();
            widget.X = Math.Max(0, Math.Min(col, Math.Max(0, cols - widget.SpanColumns)));
            widget.Y = Math.Max(0, row);
            Utilities.Logger.LogInfo("[WidgetEdit] free place " + widget.Kind + " -> " + widget.X + "," + widget.Y);
            NotifyWidgetSlots();
            SaveWidgetLayout();
        }

        /// <summary>拖拽交换两个小组件在布局里的顺序（并持久化）。</summary>
        public void SwapWidgetOrder(string kindA, string kindB)
        {
            var list = HomeWidgets.ToList();
            int ia = list.FindIndex(w => w.Kind == kindA);
            int ib = list.FindIndex(w => w.Kind == kindB);
            if (ia < 0 || ib < 0 || ia == ib) return;
            PushLayoutHistory();
            HomeWidgets.Move(ia, ib);
            Utilities.Logger.LogInfo("[WidgetEdit] drag swap " + kindA + " <-> " + kindB);
            NotifyWidgetSlots();
            SaveWidgetLayout();
        }

        /// <summary>某个种类当前是否已经启用（XAML 用它把已添加的项标成「已添加」）。</summary>
        public bool HasWidget(string kind) => FindWidget(kind) != null;

        // 默认尺寸逐条对齐 Axolotl 的 HOME_WIDGET_DEFAULT_SIZE：
        //   greeting 2x1 / recent 2x2 / calendar 1x2 / pinned-worlds 1x2 / instance 1x1
        private static string DefaultSizeFor(string kind) => kind switch
        {
            "instance" => "2x1",
            "greeting" => "2x1",
            "news" => "2x2",
            "stats" => "1x2",
            "worlds" => "1x2",
            _ => "1x1"
        };

        /// <summary>
        /// 每个种类**允许**的尺寸档（Axolotl HOME_WIDGET_SIZE_OPTIONS）。
        /// 选项菜单只列这些 —— 比如问候语固定 2x1、日历固定 1x2。
        /// </summary>
        public static string[] SizeOptionsFor(string kind) => kind switch
        {
            "greeting" => new[] { "2x1" },
            "news" => new[] { "2x1", "2x2", "3x1", "3x2" },
            "stats" => new[] { "1x2" },
            "instance" => new[] { "1x1", "2x1" },
            "worlds" => new[] { "1x1", "2x1", "1x2", "2x2" },
            "account" => new[] { "1x1", "2x1", "1x2", "2x2" },
            _ => new[] { "1x1", "2x1", "1x2", "2x2" }
        };

        /// <summary>把某个小组件设为指定尺寸（选项菜单用）。</summary>
        [CommunityToolkit.Mvvm.Input.RelayCommand]
        private void SetWidgetSize(Models.HomeWidget? widget)
        {
            // 由菜单传参：这里只做校验（真正的尺寸在 SetWidgetSizeTo）
        }

        public void SetWidgetSizeTo(Models.HomeWidget? widget, string size)
        {
            if (widget == null || string.IsNullOrEmpty(size)) return;
            if (!SizeOptionsFor(widget.Kind).Contains(size)) return;
            PushLayoutHistory();
            widget.SizeKey = size;
            Utilities.Logger.LogInfo("[WidgetEdit] size " + widget.Kind + " -> " + size);
            NotifyWidgetSlots();
            SaveWidgetLayout();
        }

        /// <summary>向前 / 向后移动（Axolotl Move earlier / Move later）。</summary>
        public void MoveWidget(Models.HomeWidget? widget, int delta)
        {
            if (widget == null) return;
            int i = HomeWidgets.IndexOf(widget);
            int j = i + delta;
            if (i < 0 || j < 0 || j >= HomeWidgets.Count) return;
            PushLayoutHistory();
            HomeWidgets.Move(i, j);
            Utilities.Logger.LogInfo("[WidgetEdit] move " + widget.Kind + " " + (delta < 0 ? "earlier" : "later"));
            NotifyWidgetSlots();
            SaveWidgetLayout();
        }

        /// <summary>首次进入用默认 6 个小件，之后沿用用户自己的布局。</summary>
        public void InitializeHomeWidgets()
        {
            // 先算「有没有实例」：它在提前 return 之后也必须是最新的
            UpdateHasVersions();

            if (HomeWidgets.Count > 0) return;

            IsHomeCompact = _configService.Settings.HomeCompact;
            Models.HomeWidget.CompactMode = IsHomeCompact;
            IsMinimalHome = _configService.Settings.HomeMinimal;
            IsFreeLayout = _configService.Settings.HomeWidgetFreeLayout;
            string saved = _configService.Settings.HomeWidgetLayout ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(saved))
            {
                foreach (var part in saved.Split(',', System.StringSplitOptions.RemoveEmptyEntries))
                {
                    var bits = part.Split(':');
                    if (bits.Length != 2) continue;
                    HomeWidgets.Add(new Models.HomeWidget { Kind = bits[0], SizeKey = bits[1] });
                }
            }

            if (HomeWidgets.Count == 0) BuildDefaultWidgets();

            // 迁移：老版本的默认布局是 greeting/instance/worlds/shortcuts，
            // 跟仪表盘实际的 5 张卡对不上 —— 直接沿用会把「今天 / 当前账号 / 新闻」整张藏掉。
            // 这里把缺的仪表盘卡片补回来（用户已有的尺寸设置不动），补过就写回配置。
            bool migrated = false;
            foreach (var kind in DashboardKinds)
            {
                if (HomeWidgets.Any(w => w.Kind == kind)) continue;
                HomeWidgets.Add(new Models.HomeWidget { Kind = kind, SizeKey = DefaultSizeFor(kind) });
                migrated = true;
            }
            if (migrated) SaveWidgetLayout();

            NotifyWidgetSlots();
        }

        /// <summary>仪表盘上真实存在的 5 张卡 —— 默认布局必须与之一致，
        /// 否则「卡片跟着 HomeWidgets 显隐」会把没列进去的卡整张藏掉。</summary>
        private static readonly string[] DashboardKinds = { "instance", "stats", "worlds", "account", "news" };

        private void BuildDefaultWidgets()
        {
            HomeWidgets.Clear();
            foreach (var kind in DashboardKinds)
                HomeWidgets.Add(new Models.HomeWidget { Kind = kind, SizeKey = DefaultSizeFor(kind) });
        }

        private void SaveWidgetLayout()
        {
            _configService.Settings.HomeWidgetLayout = string.Join(",", HomeWidgets.Select(w => w.Kind + ":" + w.SizeKey));
            _configService.SaveConfigDebounced();
        }

        [ObservableProperty]
        private string _versionTitle = "未选择版本";

        [ObservableProperty]
        private string _versionSubtitle = "请在版本列表中选择一个版本";

        [ObservableProperty]
        private string _playerName = "未登录";

        [ObservableProperty]
        private string _playerStatus = "离线";

        [ObservableProperty]
        private string _playerDetail = "尚未添加账号";

        public ObservableCollection<NewsItem> NewsItems { get; } = new ObservableCollection<NewsItem>();

        private readonly JavaService _javaService;

        private string GetString(string key)
        {
            if (Application.Current.TryFindResource(key) is string s)
            {
                return s;
            }
            return $"[{key}]";
        }

        public MainViewModel()
        {
            _configService = ConfigService.Instance;
            _accountManager = new AccountManager();

            // ── 恢复上次的所有账号 ──────────────────────────────────────
            // ⚠ 以前只恢复 SelectedAccount 一个 —— 用户切过账号再重启，
            //   另一个账号就"消失"了（其实是从来没被存下来）。
            //   现在存的是整个列表 + 一个"当前是谁"的标识。
            var cfg = _configService.Settings;

            // 兼容旧配置：只有 SelectedAccount 的，迁移进列表
            if (cfg.Accounts.Count == 0 && cfg.SelectedAccount != null)
            {
                cfg.Accounts.Add(cfg.SelectedAccount);
                cfg.SelectedAccount = null;
                cfg.CurrentAccountKey ??= AccountKey(cfg.Accounts[0]);
            }

            foreach (var acc in cfg.Accounts.ToList())
                _accountManager.AddAccount(acc);

            // 再把「上次用的是哪个」设回去
            var want = cfg.Accounts.FirstOrDefault(a => AccountKey(a) == cfg.CurrentAccountKey);
            if (want != null) _accountManager.SetCurrent(want);

            _accountManager.AccountsChanged += () =>
            {
                PersistAccounts();
                RefreshPlayerCard();
                RefreshCurrentAccountFlags();
                OnPropertyChanged(nameof(CurrentAccount));
            };

            _gameService = new GameService(_configService);

            // 版本列表是异步加载的：HasVersions 必须跟着集合实时变，
            // 否则极简主页会一直停在「还没有实例」（旧实现只在 InitializeHomeWidgets 里算一次，
            // 而那个方法在 HomeWidgets 非空时会提前 return）。
            GameVersions.CollectionChanged += OnGameVersionsChanged;
            NotificationService = new NotificationService();
            _launchService = new LaunchService(NotificationService, _configService);
            _javaService = new JavaService();

            _statusMessage = GetString("Status_Ready");
            ApplyLanguage();

            RefreshStats();
            RefreshPlayerCard();
            RefreshCurrentAccountFlags();
            RefreshJavaVersion();
            RefreshVersionTexts(_selectedVersion);

            _ = RunPreloadAsync();
        }

        /// <summary>
        /// 版本集合一变就同步 HasVersions（可能是后台线程，统一回到 UI 线程再写）。
        /// </summary>
        private void OnGameVersionsChanged(object? sender, NotifyCollectionChangedEventArgs e) => UpdateHasVersions();

        private void UpdateHasVersions()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(new Action(UpdateHasVersions));
                return;
            }

            HasVersions = GameVersions.Count > 0;
        }

        partial void OnSelectedVersionChanged(GameInstance? value)
        {
            if (value != null && _configService != null)
            {
                _configService.Settings.LastSelectedVersionId = value.Id;
                _configService.SaveConfigDebounced();
            }

            RefreshVersionTexts(value);
            RefreshLibraryStats();
        }

        /// <summary>Updates the "Minecraft 1.21.1 / 最新正式版 · Fabric …" captions.</summary>
        public void RefreshVersionTexts(GameInstance? version)
        {
            if (version == null)
            {
                VersionTitle = "未选择版本";
                VersionSubtitle = "请在版本列表中选择一个版本";
                return;
            }

            VersionTitle = version.Id;

            var parts = new List<string>();
            parts.Add(version.Type switch
            {
                "release" => "正式版",
                "snapshot" => "快照版",
                "old_beta" => "远古 Beta",
                "old_alpha" => "远古 Alpha",
                _ => version.Type
            });

            string id = version.Id.ToLowerInvariant();
            if (id.Contains("fabric")) parts.Add("Fabric");
            else if (id.Contains("neoforge")) parts.Add("NeoForge");
            else if (id.Contains("forge")) parts.Add("Forge");
            else if (id.Contains("quilt")) parts.Add("Quilt");
            if (id.Contains("optifine")) parts.Add("OptiFine");
            if (version.RequiredJavaVersion > 0) parts.Add($"Java {version.RequiredJavaVersion}+");

            VersionSubtitle = string.Join(" · ", parts);
        }

        /// <summary>Scans the selected version folder for mods/saves counts.</summary>
        public void RefreshLibraryStats()
        {
            VersionCountText = GameVersions.Count.ToString();

            int mods = 0, saves = 0;
            string dir = SelectedVersion?.GameDir ?? string.Empty;
            try
            {
                if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
                {
                    string modsDir = System.IO.Path.Combine(dir, "mods");
                    if (System.IO.Directory.Exists(modsDir))
                    {
                        mods = System.IO.Directory.GetFiles(modsDir, "*.jar").Length
                             + System.IO.Directory.GetFiles(modsDir, "*.jar.disabled").Length;
                    }

                    string savesDir = System.IO.Path.Combine(dir, "saves");
                    if (System.IO.Directory.Exists(savesDir))
                        saves = System.IO.Directory.GetDirectories(savesDir).Length;
                }
            }
            catch { }

            ModCountText = mods.ToString();
            SaveCountText = saves.ToString();
            GameDirText = string.IsNullOrEmpty(dir) ? "未设置" : dir;

            RefreshRecentWorlds(dir);
            OnPropertyChanged(nameof(GreetingText));
        }

        /// <summary>按修改时间取最近的世界（Axolotl 首页的 Recent Worlds 小组件）。</summary>
        private void RefreshRecentWorlds(string gameDir)
        {
            RecentWorlds.Clear();
            try
            {
                string savesDir = System.IO.Path.Combine(gameDir ?? string.Empty, "saves");
                if (!string.IsNullOrEmpty(gameDir) && System.IO.Directory.Exists(savesDir))
                {
                    foreach (var info in new System.IO.DirectoryInfo(savesDir).GetDirectories()
                        .OrderByDescending(d => d.LastWriteTime)
                        .Take(4))
                    {
                        RecentWorlds.Add(new Models.WorldEntry
                        {
                            Name = info.Name,
                            Path = info.FullName,
                            Detail = RelativeTime(info.LastWriteTime)
                        });
                    }
                }
            }
            catch { }
            HasRecentWorlds = RecentWorlds.Count > 0;
        }

        private static string RelativeTime(System.DateTime time)
        {
            var span = System.DateTime.Now - time;
            if (span.TotalMinutes < 1) return "刚刚";
            if (span.TotalHours < 1) return (int)span.TotalMinutes + " 分钟前";
            if (span.TotalDays < 1) return (int)span.TotalHours + " 小时前";
            if (span.TotalDays < 30) return (int)span.TotalDays + " 天前";
            return time.ToString("yyyy-MM-dd");
        }

        /// <summary>Recomputes play-time statistics shown on the dashboard.</summary>
        public void RefreshStats()
        {
            var s = _configService.Settings;

            PlayTimeText = s.TotalPlaySeconds >= 3600
                ? $"{s.TotalPlaySeconds / 3600.0:F1} h"
                : $"{s.TotalPlaySeconds / 60} min";

            LaunchCountText = s.LaunchCount.ToString();

            string[] week = { "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六" };
            var now = System.DateTime.Now;
            TodayText = now.Month + "月" + now.Day + "日 " + week[(int)now.DayOfWeek];

            bool launchedToday = false;
            if (!string.IsNullOrEmpty(s.LastLaunchUtc) &&
                System.DateTime.TryParse(s.LastLaunchUtc, out var last))
                launchedToday = last.ToLocalTime().Date == now.Date;
            TodayPlayText = launchedToday ? "今天玩过" : "未游玩";
            LastPlayedText = FormatRelativeTime(s.LastLaunchUtc);
        }

        private static string FormatRelativeTime(string? isoUtc)
        {
            if (string.IsNullOrEmpty(isoUtc)) return "从未";
            if (!DateTime.TryParse(isoUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out var t))
                return "从未";

            var span = DateTime.UtcNow - t.ToUniversalTime();
            if (span.TotalSeconds < 60) return "刚刚";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} 分钟前";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} 小时前";
            return $"{(int)span.TotalDays} 天前";
        }

        /// <summary>Fills the player card from the active account.</summary>
        public void RefreshPlayerCard()
        {
            var acc = AccountManager?.CurrentAccount;
            if (acc == null)
            {
                PlayerName = "未登录";
                PlayerStatus = "离线";
                PlayerDetail = "尚未添加账号";
                return;
            }

            PlayerName = acc.Username;
            bool ms = acc.Type == AccountType.Microsoft;
            PlayerStatus = ms ? "正版" : "离线";
            string tail = string.IsNullOrEmpty(acc.Uuid)
                ? "—"
                : acc.Uuid.Replace("-", "")[^Math.Min(4, acc.Uuid.Replace("-", "").Length)..];
            PlayerDetail = ms ? $"正版账户 · UUID 尾号 {tail}" : $"离线账户 · UUID 尾号 {tail}";
        }

        /// <summary>Detects the best available Java for the dashboard (background).</summary>
        public void RefreshJavaVersion()
        {
            _ = Task.Run(() =>
            {
                string text = "未检测";
                try
                {
                    var installs = new JavaService().FindInstallations();
                    if (installs.Count > 0)
                    {
                        var best = installs
                            .OrderByDescending(j => new JavaService().GetMajorVersion(j.Version))
                            .First();
                        text = best.Version ?? "已安装";
                        // Trim verbose vendor strings like "17.0.5+8-LTS"
                        int cut = text.IndexOf('+');
                        if (cut > 0) text = text[..cut];
                    }
                }
                catch { }

                Application.Current?.Dispatcher.Invoke(() => JavaVersionText = text);
            });
        }

        /// <summary>Builds the activity feed from the preloaded manifest / Modrinth data.</summary>
        public void RefreshNews()
        {
            var items = new List<NewsItem>();

            try
            {
                var versions = PreloadService.CachedVersionList;
                var latest = versions?.FirstOrDefault(v => v.Type == "release") ?? versions?.FirstOrDefault();
                if (latest != null)
                {
                    items.Add(new NewsItem
                    {
                        Icon = "Sparkle24",
                        Title = $"Minecraft {latest.Id} 已发布",
                        Detail = $"最新正式版 · {latest.ReleaseTime:yyyy-MM-dd}"
                    });
                }

                foreach (var mod in PreloadService.CachedTrendingMods.Take(2))
                {
                    items.Add(new NewsItem
                    {
                        Icon = "PuzzlePiece24",
                        Title = mod.Name,
                        Detail = $"热门 Mod · {FormatDownloads(mod.Downloads)} 次下载",
                        Url = mod.WebUrl
                    });
                }
            }
            catch { }

            if (items.Count == 0)
            {
                items.Add(new NewsItem { Icon = "News24", Title = "暂无动态", Detail = "联网后自动加载版本与 Mod 资讯" });
            }

            NewsItems.Clear();
            foreach (var item in items.Take(3)) NewsItems.Add(item);
        }

        private static string FormatDownloads(long n) => n switch
        {
            >= 1_000_000_000 => $"{n / 1_000_000_000.0:F1}B",
            >= 1_000_000 => $"{n / 1_000_000.0:F1}M",
            >= 1_000 => $"{n / 1_000.0:F1}K",
            _ => n.ToString()
        };

        private async Task RunPreloadAsync()
        {
            IsPreloading = true;

            BackgroundOpacity = _configService.Settings.BackgroundOpacity;
            BlurEffectRadius = _configService.Settings.BlurEffectRadius;

            try
            {
                await LoadBackgroundAsync();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Load background failed: {ex.Message}");
                BackgroundImage = PreloadService.LoadDefaultBackground();
            }

            PreloadService.ProgressChanged += (s, e) =>
            {
                int progress = (int)(e.OverallProgress * 100);
                PreloadProgressChanged?.Invoke(progress, e.CurrentTask);
            };

            try
            {
                await PreloadService.PreloadAllAsync();
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Preload failed: {ex.Message}");
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                IsPreloading = false;
                RefreshNews();
                PreloadCompleted?.Invoke();
            });
        }

        private void ApplyLanguage()
        {
            if (!string.IsNullOrEmpty(_configService.Settings.Language))
            {
                App.SwitchLanguage(_configService.Settings.Language);
            }
        }

        public async Task LoadBackgroundAsync()
        {
            IsLoadingBackground = true;
            try
            {
                string? path = _configService.Settings.BackgroundImagePath;
                BackgroundOpacity = _configService.Settings.BackgroundOpacity;
                BlurEffectRadius = _configService.Settings.BlurEffectRadius;

                if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
                {
                    byte[] imageData = await Task.Run(() => System.IO.File.ReadAllBytesAsync(path));
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 1920;
                    bitmap.StreamSource = new System.IO.MemoryStream(imageData);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    BackgroundImage = bitmap;
                }
                else
                {
                    var bitmap = new BitmapImage(new System.Uri("pack://application:,,,/Assets/Images/cirno_bg.png"));
                    bitmap.Freeze();
                    BackgroundImage = bitmap;
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading background: {ex.Message}");
                var bitmap = new BitmapImage(new System.Uri("pack://application:,,,/Assets/Images/cirno_bg.png"));
                bitmap.Freeze();
                BackgroundImage = bitmap;
            }
            finally
            {
                IsLoadingBackground = false;
            }
        }

        [RelayCommand]
        private async Task PickBackgroundImageAsync()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Background Image",
                Filter = "Image Files|*.png;*.jpg;*.jpeg;*.bmp" // WPF has no native WebP support
            };

            if (dialog.ShowDialog() == true)
            {
                _configService.Settings.BackgroundImagePath = dialog.FileName;
                _configService.SaveConfigDebounced();
                await LoadBackgroundAsync();
            }
        }
        
        public async Task LoadVersionsAsync()
        {
            await Task.Run(() =>
            {
                string dotMinecraft = _configService.Settings.GamePath;
                if (string.IsNullOrEmpty(dotMinecraft) || !System.IO.Directory.Exists(dotMinecraft))
                {
                     dotMinecraft = System.IO.Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), ".minecraft");
                }
                
                _configService.Settings.GamePath = dotMinecraft;
                _configService.SaveConfigDebounced();

                if (!System.IO.Directory.Exists(dotMinecraft))
                {
                    try { System.IO.Directory.CreateDirectory(dotMinecraft); } catch {}
                }
                
                var versions = _gameService.ScanVersions(dotMinecraft, _configService.Settings.VersionIsolation);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    string? prevSelectedId = SelectedVersion?.Id;
                    GameVersions.Clear();
                    foreach(var v in versions) GameVersions.Add(v);
                    
                    if (prevSelectedId != null)
                    {
                        var match = GameVersions.FirstOrDefault(v => v.Id == prevSelectedId);
                        SelectedVersion = match ?? GameVersions.FirstOrDefault();
                    }
                    else if (GameVersions.Any())
                    {
                        // First launch of the session: restore the last used version
                        var lastId = _configService.Settings.LastSelectedVersionId;
                        var lastMatch = !string.IsNullOrEmpty(lastId)
                            ? GameVersions.FirstOrDefault(v => v.Id == lastId)
                            : null;
                        SelectedVersion = lastMatch ?? GameVersions.First();
                    }
                });
            });

            // 加载完成后强制同步一次（集合事件之外的双保险），并刷新首页文案
            UpdateHasVersions();
            RefreshVersionTexts(SelectedVersion);
            TsuruLauncher.Utilities.Logger.LogInfo(
                $"[Versions] loaded count={GameVersions.Count} hasVersions={HasVersions} selected={SelectedVersion?.Id ?? "<none>"}");
        }

        [RelayCommand]
        private async Task LaunchGame()
        {
            TsuruLauncher.Utilities.Logger.LogInfo($"[Launch] LaunchGame invoked version={SelectedVersion?.Id ?? "<none>"}");

            if (SelectedVersion == null) { iOS26Dialog.Show(GetString("Msg_NoVersion")); return; }
            if (AccountManager.CurrentAccount == null) { iOS26Dialog.Show(GetString("Msg_NoAccount")); return; }

            string versionJsonPath = System.IO.Path.Combine(SelectedVersion.RootPath, "versions", SelectedVersion.Id, $"{SelectedVersion.Id}.json");
            if (!System.IO.File.Exists(versionJsonPath))
            {
                iOS26Dialog.Show($"版本 \"{SelectedVersion.Id}\" 的文件已不存在，可能已被外部删除。\n请刷新版本列表。", "版本缺失", DialogIcon.Warning);
                await LoadVersionsAsync();
                return;
            }

            // Resolve Java Path
            string javaPath = SelectedVersion.CustomJavaPath;

            // 1. Smart Selection: If no custom path, try to find the best matching Java version automatically
            if (string.IsNullOrWhiteSpace(javaPath))
            {
                // Re-scan so a Java installed since the last scan is found
                _javaService.ClearCache();
                // Pass the required version (e.g. 8 or 17) to find a specific match
                var bestMatch = _javaService.AutoDetectBestJava(SelectedVersion.RequiredJavaVersion);
                if (!string.IsNullOrEmpty(bestMatch))
                {
                   javaPath = bestMatch;
                }
            }

            // 2. Fallback to Global if auto-detect didn't find a specific match
            if (string.IsNullOrWhiteSpace(javaPath))
            {
                javaPath = _configService.Settings.JavaPath;
            }

            // 3. Final Check: If still missing or invalid, try to find ANY Java (last resort)
            if (string.IsNullOrWhiteSpace(javaPath) || !System.IO.File.Exists(javaPath))
            {
                var detected = _javaService.AutoDetectBestJava(0); 
                if (detected != null)
                {
                    javaPath = detected;
                    // Only save to Global if Global was empty
                    if (string.IsNullOrWhiteSpace(_configService.Settings.JavaPath))
                    {
                        _configService.Settings.JavaPath = javaPath;
                        _configService.SaveConfigDebounced();
                    }
                }
                else
                {
                    // Nothing usable locally — offer to fetch a matching runtime
                    int need = SelectedVersion.RequiredJavaVersion;
                    bool accepted = iOS26Dialog.Show(
                        $"未找到可用的 Java {need}。\n\n是否自动下载并安装 Java {need} 运行时？\n（来自 Adoptium / Eclipse Temurin，约 40–60 MB）",
                        "需要 Java 运行时", DialogIcon.Warning, DialogButtons.YesNo) == true;

                    if (!accepted)
                    {
                        iOS26Dialog.Show(string.Format(GetString("Msg_JavaMissing"), need), "未找到 Java", DialogIcon.Warning);
                        return;
                    }

                    try
                    {
                        IsLaunching = true;
                        StatusMessage = $"正在准备 Java {need}...";
                        var javaStatus = new Progress<string>(s =>
                            Application.Current.Dispatcher.Invoke(() => StatusMessage = s));
                        var javaProgress = new Progress<double>(_ => { });

                        javaPath = await Services.JavaRuntimeService.EnsureAsync(need, javaProgress, javaStatus);
                        _javaService.ClearCache();

                        _configService.Settings.JavaPath = javaPath;
                        _configService.SaveConfigDebounced();
                    }
                    catch (System.Exception ex)
                    {
                        IsLaunching = false;
                        StatusMessage = GetString("Status_Ready");
                        iOS26Dialog.Show($"Java 自动安装失败：\n{ex.Message}", "错误", DialogIcon.Error);
                        return;
                    }
                }
            }
            
            try 
            {
               IsLaunching = true;
               StatusMessage = GetString("Status_Checking");
               TsuruLauncher.Utilities.Logger.LogInfo(
                   $"[Launch] state IsLaunching={IsLaunching} status={StatusMessage} java={javaPath}");

               // Pre-launch checks
               var analyzer = new CrashAnalyzerService();
               string? conflictWarning = await analyzer.CheckForConflictsAsync(SelectedVersion.GameDir);
               if (conflictWarning != null)
               {
                   if (iOS26Dialog.Show($"潜在模组冲突:\n{conflictWarning}\n\n是否继续？", GetString("Title_Warning"), DialogIcon.Warning, DialogButtons.YesNo) != true)
                   {
                       IsLaunching = false;
                       StatusMessage = GetString("Status_Ready");
                       return;
                   }
               }

               // 实时日志窗口（PCL2 风格）：显示启动参数、stdout / stderr 与退出码
               Services.LaunchLogHub.BeginSession(SelectedVersion.Id);
               if (_configService.Settings.ShowLaunchLog)
               {
                   Views.LaunchLogWindow.ShowWindow(SelectedVersion.Id);
               }

               var process = await _launchService.LaunchGameAsync(SelectedVersion, AccountManager.CurrentAccount, _configService.Settings.MaxRam, javaPath, 
                   (status) => 
                   {
                       // Update status from LaunchService
                       Services.LaunchLogHub.SetStatus(status, -1);
                       Application.Current.Dispatcher.Invoke(() => StatusMessage = status);
                   },
                   (progress) => 
                   {
                       // Update progress (if needed)
                       Application.Current.Dispatcher.Invoke(() => 
                       {
                           Services.LaunchLogHub.SetStatus("", progress);
                       });
                   });

               if (process != null)
               {
                   // Record launch statistics for the dashboard
                   var launchStart = DateTime.UtcNow;
                   _configService.Settings.LaunchCount++;
                   _configService.Settings.LastLaunchUtc = launchStart.ToString("o");
                   _configService.SaveConfigDebounced();
                   RefreshStats();

                   // Apply Launcher Visibility
                   int visibility = _configService.Settings.LauncherVisibility;
                   if (visibility == 2) // Close
                   {
                       Application.Current.Shutdown();
                       return;
                   }
                   
                   if (visibility == 1) // Hide
                   {
                       Application.Current.MainWindow.Hide();
                   }

                   StatusMessage = GetString("Status_Running");
                   await process.WaitForExitAsync();

                   // Accumulate play time when the game exits
                   long played = (long)(DateTime.UtcNow - launchStart).TotalSeconds;
                   if (played > 0 && played < 24 * 3600)
                   {
                       _configService.Settings.TotalPlaySeconds += played;
                       _configService.SaveConfigDebounced();
                   }
                   RefreshStats();
                   
                   // Restore Visibility if Hidden
                   if (visibility == 1)
                   {
                       Application.Current.MainWindow.Show();
                       Application.Current.MainWindow.WindowState = WindowState.Normal;
                       Application.Current.MainWindow.Activate();
                   }

                   IsLaunching = false;
                   StatusMessage = GetString("Status_Ready");
                   Services.LaunchLogHub.EndSession("游戏已退出（退出码 " + process.ExitCode + "）");

                   if (process.ExitCode != 0)
                   {
                       var crashAnalyzer = new CrashAnalyzerService();
                       var result = await crashAnalyzer.AnalyzeAsync(SelectedVersion.GameDir);
                       
                       if (result.IsCrashDetected)
                       {
                           iOS26Dialog.Show($"游戏崩溃！\n\n原因: {result.Cause}\n解决方案: {result.Solution}", GetString("Title_Crash"), DialogIcon.Error);
                       }
                       else
                       {
                            iOS26Dialog.Show($"游戏退出，退出码: {process.ExitCode}。请查看日志获取详情。", GetString("Title_GameExited"), DialogIcon.Warning);
                       }
                   }
               }
               else
               {
                   // Process failed to start: restore UI state so the user can retry
                   IsLaunching = false;
                   StatusMessage = GetString("Status_Ready");
                   Services.LaunchLogHub.EndSession("启动失败：游戏进程没有成功拉起");
               }
            }
            catch (System.Exception ex)
            {
                // Ensure window is shown if launch fails
                Application.Current.MainWindow.Show();
                
                IsLaunching = false;
                StatusMessage = GetString("Status_Failed");
                Services.LaunchLogHub.EndSession("启动失败：" + ex.Message);
                iOS26Dialog.Show($"启动失败: {ex.Message}", "错误", DialogIcon.Error);
            }
        }
        [RelayCommand]
        private async Task ExportModpack()
        {
            if (SelectedVersion == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Modrinth Modpack (*.mrpack)|*.mrpack",
                FileName = $"{SelectedVersion.Id}.mrpack",
                Title = "Export Modpack"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var modpackService = new TsuruLauncher.Services.Ecosystem.ModpackService();
                    await modpackService.ExportMrPackAsync(SelectedVersion, dialog.FileName);
                    iOS26Dialog.Show(GetString("Title_ExportComplete"), "导出完成", DialogIcon.Success);
                }
                catch (System.Exception ex)
                {
                    iOS26Dialog.Show($"导出失败: {ex.Message}", "错误", DialogIcon.Error);
                }
            }
        }
        [RelayCommand]
        private async Task UploadLog()
        {
            if (SelectedVersion == null) return;
            string logPath = System.IO.Path.Combine(SelectedVersion.GameDir, "logs", "latest.log");

            if (!System.IO.File.Exists(logPath))
            {
                iOS26Dialog.Show(GetString("Msg_LogNotFound"), GetString("Title_Warning"), DialogIcon.Info);
                return;
            }

            try
            {
                iOS26Dialog.Show(GetString("Msg_UploadNotImplemented"), GetString("Title_Warning"), DialogIcon.Info);
                await Task.CompletedTask;
            }
            catch (System.Exception ex)
            {
                iOS26Dialog.Show($"上传失败: {ex.Message}", "错误", DialogIcon.Error);
            }
        }
        public event System.Action<object>? RequestNavigation;
        public event System.Action? RequestGoBack;

        [RelayCommand]
        private void OpenLaunchSettings()
        {
            if (SelectedVersion == null) return;
            // Triggers navigation in MainWindow
            RequestNavigation?.Invoke(new TsuruLauncher.Views.VersionSettingsPage(SelectedVersion));
        }

        [RelayCommand]
        private void SaveVersionSettings()
        {
            if (SelectedVersion != null)
            {
                _gameService.SaveVersionConfig(SelectedVersion);
            }
            RequestGoBack?.Invoke();
        }

        [RelayCommand]
        private void NavigateBack()
        {
            RequestGoBack?.Invoke();
        }
    }
}