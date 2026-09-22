using System;
using System.IO;
using Newtonsoft.Json;
using TsuruLauncher.Models;

namespace TsuruLauncher.Services
{
    public class AppConfig
    {
        public string JavaPath { get; set; } = string.Empty;
        public string GamePath { get; set; } = string.Empty;
        public int MaxRam { get; set; } = 4096;
        public int MinRam { get; set; } = 512;
        public bool AutoDetectMemory { get; set; } = true;
        public int WindowWidth { get; set; } = 854;
        public int WindowHeight { get; set; } = 480;
        public bool Fullscreen { get; set; } = false;
        public string DownloadSource { get; set; } = "Official";
        public Account? SelectedAccount { get; set; }
        public bool VersionIsolation { get; set; } = true;
        public string Language { get; set; } = "en-US";
        public string? LastSelectedVersionId { get; set; }

        public string? BackgroundImagePath { get; set; }
        public double BackgroundOpacity { get; set; } = 0.6;
        public double BlurEffectRadius { get; set; } = 0;

        public int LauncherVisibility { get; set; } = 0;
        public int ProcessPriority { get; set; } = 0;

        public int MaxDownloadThreads { get; set; } = 64;

        /// <summary>启动游戏时自动弹出实时日志窗口（PCL2 风格）。</summary>
        public bool ShowLaunchLog { get; set; } = true;

        /// <summary>首页小组件布局："kind:size,kind:size"，空则用默认布局。</summary>
        public bool HomeWidgetFreeLayout { get; set; }
        public string HomeWidgetLayout { get; set; } = string.Empty;

        /// <summary>首页模式：true = 极简主页，false = 信息主页（小组件）。</summary>
        public bool HomeMinimal { get; set; } = true;

        /// <summary>首页紧凑模式（行高缩小）。</summary>
        public bool HomeCompact { get; set; }

        /// <summary>CurseForge 官方 API Key（用户自备，用于第二个内容源）。</summary>
        public string CurseForgeApiKey { get; set; } = string.Empty;

        public string ModrinthApiBaseUrl { get; set; } = "https://api.modrinth.com/v2/";

        public string GlobalJvmArguments { get; set; } = string.Empty;
        public string GlobalGameArguments { get; set; } = string.Empty;
        public string CustomWindowTitle { get; set; } = string.Empty;

        public List<string> HiddenPageKeys { get; set; } = new List<string>();

        /// <summary>Legacy single-palette setting (kept only to migrate old configs).</summary>
        public string Theme { get; set; } = string.Empty;

        /// <summary>Appearance mode: "dark" or "oled" (see ThemeService).</summary>
        public string ThemeMode { get; set; } = "dark";

        /// <summary>Accent setting: preset key, "#rrggbb" or "system".</summary>
        public string AccentColor { get; set; } = "green";

        // ---- Play statistics (home dashboard) ----
        public int LaunchCount { get; set; } = 0;
        public long TotalPlaySeconds { get; set; } = 0;
        public string? LastLaunchUtc { get; set; }
    }

    public class ConfigService
    {
        private const string ConfigFileName = "config.json";
        private readonly string _configPath;
        private static readonly Lazy<ConfigService> _instance = new(() => new ConfigService(), System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
        public static ConfigService Instance => _instance.Value;

        public AppConfig Settings { get; private set; }

        public ConfigService()
        {
            _configPath = ResolveConfigPath();
            Settings = LoadConfig();

            if (MigrateLegacyTheme(Settings))
            {
                try { SaveConfig(); } catch { }
            }
        }

        /// <summary>
        /// Older versions stored one of several hand-tuned palettes in
        /// <see cref="AppConfig.Theme"/>. Map those onto the mode + accent model
        /// once, then clear the legacy value.
        /// </summary>
        private static bool MigrateLegacyTheme(AppConfig cfg)
        {
            if (string.IsNullOrWhiteSpace(cfg.Theme)) return false;

            string legacy = cfg.Theme.Trim().ToLowerInvariant();
            cfg.ThemeMode = legacy == "oled" ? "oled" : "dark";
            cfg.AccentColor = legacy switch
            {
                "abyss" => "#5B9BF8",
                "aurora" => "violet",
                "frost" => "cyan",
                "lava" => "orange",
                "royal" => "amber",
                "sakura" => "rose",
                "axolotl" => "rose",
                "obsidian" => "#94A3B8",
                _ => "green"
            };
            cfg.Theme = string.Empty;
            return true;
        }

        /// <summary>
        /// Config lives in %APPDATA%\TsuruLauncher so rebuilding / updating the
        /// launcher (which wipes the output folder) never loses user settings.
        /// A config next to the executable is migrated automatically.
        /// </summary>
        private static string ResolveConfigPath()
        {
            string legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ConfigFileName);

            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TsuruLauncher");
                Directory.CreateDirectory(dir);
                string newPath = Path.Combine(dir, ConfigFileName);

                if (!File.Exists(newPath) && File.Exists(legacyPath))
                {
                    try { File.Copy(legacyPath, newPath); } catch { }
                }

                return newPath;
            }
            catch
            {
                return legacyPath;
            }
        }

        private AppConfig LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                return new AppConfig();
            }


            try
            {
                string json = File.ReadAllText(_configPath);
                return JsonConvert.DeserializeObject<AppConfig>(json) ?? new AppConfig();
            }
            catch
            {
                return new AppConfig();
            }
        }

        public void SaveConfig()
        {
            try
            {
                string json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                TsuruLauncher.Utilities.Logger.LogError(ex, "ConfigService.SaveConfig");
            }
        }

        private System.Windows.Threading.DispatcherTimer? _debounceTimer;
        private readonly object _debounceLock = new object();

        /// <summary>
        /// **合并写盘** —— UI 上高频开关（首页模式 ▦/📋、紧凑模式、小组件布局）走的入口。
        ///
        /// 以前这些路径每点一次都同步跑 <c>JsonConvert.SerializeObject + File.WriteAllText</c>，
        /// 实测单次 1.2~3.1ms，直接把「点击同步阻塞 &lt; 3ms」的预算吃光
        /// （连点 ▦/📋 时实测出现 3.12ms 的 OVER-BUDGET —— 那 3ms 全在写盘，不在动画）。
        /// 改成 400ms 静默窗口合并成一次写盘后，交互路径回到亚毫秒级；
        /// 进程退出前由 <see cref="FlushPendingSave"/> 保证一定落盘。
        /// </summary>
        public void SaveConfigDebounced()
        {
            try
            {
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher == null) { SaveConfig(); return; }

                lock (_debounceLock)
                {
                    if (_debounceTimer == null)
                    {
                        _debounceTimer = new System.Windows.Threading.DispatcherTimer(
                            TimeSpan.FromMilliseconds(400),
                            System.Windows.Threading.DispatcherPriority.Background,
                            (_, __) =>
                            {
                                lock (_debounceLock) { _debounceTimer?.Stop(); }
                                SaveConfig();
                            },
                            dispatcher);
                    }
                    _debounceTimer.Stop();
                    _debounceTimer.Start();
                }
            }
            catch
            {
                try { SaveConfig(); } catch { }
            }
        }

        /// <summary>把还压着的合并写盘立刻落下去（窗口关闭 / 进程退出前调用）。</summary>
        public void FlushPendingSave()
        {
            bool pending = false;
            lock (_debounceLock)
            {
                if (_debounceTimer != null && _debounceTimer.IsEnabled)
                {
                    _debounceTimer.Stop();
                    pending = true;
                }
            }
            if (pending) SaveConfig();
        }
    }
}