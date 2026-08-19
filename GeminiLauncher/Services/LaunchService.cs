using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Compression;
using GeminiLauncher.Models;
using GeminiLauncher.Services.Network;
using GeminiLauncher.Utilities;
using Newtonsoft.Json.Linq;

namespace GeminiLauncher.Services
{
    public class LaunchService
    {
        private readonly NotificationService _notificationService;
        private readonly ConfigService _configService;

        public LaunchService(NotificationService notificationService, ConfigService configService)
        {
            _notificationService = notificationService;
            _configService = configService;
        }

        public static long GetTotalSystemMemoryMB()
        {
            try
            {
                var mos = new System.Management.ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
                foreach (var obj in mos.Get())
                {
                    var bytes = Convert.ToInt64(obj["TotalPhysicalMemory"]);
                    return bytes / 1024 / 1024;
                }
            }
            catch { }
            return 8192;
        }

        public async Task<Process?> LaunchGameAsync(GameInstance game, Account account, int globalMaxRamMb, string globalJavaPath, Action<string>? statusCallback = null, Action<int>? progressCallback = null)
        {
            if (string.IsNullOrEmpty(account.Username) || string.IsNullOrEmpty(account.Uuid) || string.IsNullOrEmpty(account.AccessToken))
            {
                throw new ArgumentException("Invalid Account: Username, UUID, or Access Token is missing.");
            }

            // Refresh a stale Microsoft session before launch
            if (account.Type == AccountType.Microsoft &&
                account.ExpiryTime > 0 &&
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 120 >= account.ExpiryTime)
            {
                statusCallback?.Invoke("刷新账号令牌...");
                try
                {
                    var authService = new AuthenticationService();
                    await authService.RefreshSessionAsync(account);
                }
                catch (Exception ex)
                {
                    var refreshMsg = $"微软账号登录已过期且刷新失败: {ex.Message}";
                    _notificationService.Show("启动失败", refreshMsg, NotificationType.Error);
                    throw new Exception(refreshMsg);
                }
            }

            statusCallback?.Invoke("校验资源文件...");
            await ValidateAndRepairAsync(game, statusCallback);

            int minRamMb, maxRamMb;
            ResolveMemorySettings(game, globalMaxRamMb, out minRamMb, out maxRamMb);

            string javaPath = game.UseGlobalSettings ? globalJavaPath : game.CustomJavaPath;
            if (string.IsNullOrEmpty(javaPath)) javaPath = globalJavaPath;
            if (string.IsNullOrEmpty(javaPath)) throw new ArgumentException("Java path is missing!");

            statusCallback?.Invoke("检查 Java 环境...");
            var javaService = new JavaService();

            if (!File.Exists(javaPath))
            {
                var msg = $"Java 路径不存在: {javaPath}";
                _notificationService.Show("启动失败", msg, NotificationType.Error);
                throw new Exception(msg);
            }

            JavaInstallation? javaInfo = null;
            try
            {
                javaInfo = javaService.GetJavaInfo(javaPath);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "GetJavaInfo");
            }
            int majorVer = javaInfo != null ? javaService.GetMajorVersion(javaInfo.Version) : 0;

            if (javaInfo == null || (game.RequiredJavaVersion > 0 && majorVer < game.RequiredJavaVersion))
            {
                var bestJava = javaService.AutoDetectBestJava(game.RequiredJavaVersion);
                if (!string.IsNullOrEmpty(bestJava))
                {
                    javaPath = bestJava;
                    _notificationService.Show("Java 自动切换", $"已自动切换至适配 Java", NotificationType.Info);
                }
                else
                {
                    var msg = $"游戏需要 Java {game.RequiredJavaVersion}+，但未找到可用版本。";
                    _notificationService.Show("启动失败", msg, NotificationType.Error);
                    throw new Exception(msg);
                }
            }

            string librariesPath = Path.Combine(game.RootPath, "libraries");
            string assetsPath = Path.Combine(game.RootPath, "assets");
            string nativesPath = Path.Combine(game.GameDir, "natives");
            string logFile = Path.Combine(game.GameDir, "gemini_launch_log.txt");

            try { File.WriteAllText(logFile, $"--- Launch Log {DateTime.Now} ---\n"); } catch {}
            void Log(string m) { Logger.LogDebug(m); try { File.AppendAllText(logFile, m + "\n"); } catch {} }

            Log($"[Config] MinRAM={minRamMb}M MaxRAM={maxRamMb}M Java={javaPath}");

            statusCallback?.Invoke("解压运行库...");
            ExtractNatives(game, librariesPath, nativesPath, Log);

            string classpath = BuildClasspath(game, librariesPath, Log);

            statusCallback?.Invoke("构建启动参数...");
            var sbArgs = new StringBuilder();

            BuildJvmArguments(sbArgs, game, account, assetsPath, nativesPath, logFile,
                minRamMb, maxRamMb, classpath, Log);

            BuildGameArguments(sbArgs, game, account, Log);

            statusCallback?.Invoke("启动游戏进程...");
            Process? process = StartGameProcess(javaPath, sbArgs.ToString().Trim(), game.GameDir, nativesPath, Log);
            if (process == null) return null;

            WireProcessOutput(process, Log);
            return process;
        }

        private void ResolveMemorySettings(GameInstance game, int globalMaxRam, out int minRam, out int maxRam)
        {
            if (!game.UseGlobalSettings)
            {
                maxRam = game.CustomMemoryMb > 0 ? game.CustomMemoryMb : 2048;
                minRam = game.CustomMinMemoryMb > 0 ? game.CustomMinMemoryMb : Math.Min(512, maxRam / 4);
            }
            else
            {
                maxRam = globalMaxRam > 0 ? globalMaxRam : 4096;
                minRam = _configService.Settings.MinRam > 0 ? _configService.Settings.MinRam : 512;
            }

            if (_configService.Settings.AutoDetectMemory)
            {
                long totalMem = GetTotalSystemMemoryMB();
                long recommended = totalMem * 50 / 100;
                recommended = Math.Max(1024, Math.Min(recommended, totalMem - 1024));
                if (maxRam > recommended || maxRam <= 0)
                    maxRam = (int)Math.Max(2048, recommended);
                minRam = Math.Min(minRam, maxRam / 3);
            }

            // Clamp max first, then clamp min against the FINAL max so -Xms can never exceed -Xmx
            int systemMemoryMb = Math.Max(512, (int)GetTotalSystemMemoryMB());
            maxRam = Math.Clamp(maxRam, 512, systemMemoryMb);
            minRam = Math.Clamp(minRam, 256, maxRam);
        }

        private void ExtractNatives(GameInstance game, string librariesPath, string nativesPath, Action<string> Log)
        {
            if (Directory.Exists(nativesPath))
                try { Directory.Delete(nativesPath, true); } catch {}
            Directory.CreateDirectory(nativesPath);

            foreach (var lib in game.Libraries)
            {
                string fullPath = Path.Combine(librariesPath, lib.Path.Replace('/', Path.DirectorySeparatorChar));
                bool isNative = lib.Name.Contains("natives-windows") || lib.Path.Contains("natives-windows");

                if (File.Exists(fullPath) && isNative)
                {
                    try
                    {
                        using (var archive = ZipFile.OpenRead(fullPath))
                        {
                            foreach (var entry in archive.Entries)
                            {
                                if ((entry.FullName.EndsWith(".dll") || entry.FullName.EndsWith(".so") || entry.FullName.EndsWith(".dylib"))
                                    && !entry.FullName.Contains("META-INF"))
                                {
                                    entry.ExtractToFile(Path.Combine(nativesPath, entry.Name), true);
                                }
                            }
                        }
                        Log($"[Native] OK: {lib.Name}");
                    }
                    catch (Exception ex) { Log($"[Native] FAIL {lib.Name}: {ex.Message}"); }
                }
            }
        }

        private string BuildClasspath(GameInstance game, string librariesPath, Action<string> Log)
        {
            var sbCp = new StringBuilder();
            int count = 0;
            foreach (var lib in game.Libraries)
            {
                string fullPath = Path.Combine(librariesPath, lib.Path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath))
                {
                    sbCp.Append(fullPath).Append(Path.PathSeparator);
                    count++;
                }
                else
                {
                    Log($"[Lib] Missing: {lib.Name}");
                }
            }
            string clientJar = Path.Combine(game.RootPath, "versions", game.Id, $"{game.Id}.jar");
            if (!File.Exists(clientJar))
                Log($"[Lib] Missing client jar: {clientJar}");
            sbCp.Append(clientJar);
            Log($"[Classpath] {count} libs + client jar");
            return sbCp.ToString();
        }

        private void BuildJvmArguments(StringBuilder sbArgs, GameInstance game, Account account,
            string assetsPath, string nativesPath, string logFile,
            int minRamMb, int maxRamMb, string classpath, Action<string> Log)
        {
            sbArgs.Append($"-Xms{minRamMb}M ");
            sbArgs.Append($"-Xmx{maxRamMb}M ");

            sbArgs.Append("-XX:+UseG1GC ");
            sbArgs.Append("-XX:MaxGCPauseMillis=200 ");
            sbArgs.Append("-XX:+UnlockExperimentalVMOptions ");
            sbArgs.Append("-XX:G1NewSizePercent=20 ");
            sbArgs.Append("-XX:G1ReservePercent=20 ");

            sbArgs.Append($"-Djava.library.path=\"{nativesPath}\" ");
            sbArgs.Append($"-Dorg.lwjgl.librarypath=\"{nativesPath}\" ");

            if (_configService.Settings.WindowWidth > 0 && _configService.Settings.WindowHeight > 0)
            {
                sbArgs.Append($"-Dminecraft.launcher.width={_configService.Settings.WindowWidth} ");
                sbArgs.Append($"-Dminecraft.launcher.height={_configService.Settings.WindowHeight} ");
            }
            if (!string.IsNullOrEmpty(_configService.Settings.CustomWindowTitle))
            {
                string title = _configService.Settings.CustomWindowTitle;
                if (title.Contains(" ")) title = $"\"{title}\"";
                sbArgs.Append($"-Dminecraft.launcher.title={title} ");
            }

            if (!string.IsNullOrEmpty(_configService.Settings.GlobalJvmArguments))
                sbArgs.Append(_configService.Settings.GlobalJvmArguments).Append(" ");

            // Per-version JVM arguments (lyzl_profile.json) — only when the version
            // is not using global settings
            if (!game.UseGlobalSettings && !string.IsNullOrEmpty(game.CustomJvmArgs))
                sbArgs.Append(ReplacePlaceholders(game.CustomJvmArgs, game, account, assetsPath, classpath)).Append(" ");

            if (game.LogConfig != null && !string.IsNullOrEmpty(game.LogConfig.Argument))
            {
                string logConfigPath = Path.Combine(assetsPath, "log_configs", game.LogConfig.File.Id);
                if (File.Exists(logConfigPath))
                {
                    string logArg = game.LogConfig.Argument.Replace("${path}", logConfigPath);
                    sbArgs.Append(logArg).Append(" ");
                    Log($"[Log4j] {logArg}");
                }
            }

            foreach (var arg in game.JvmArguments)
            {
                string val = ReplacePlaceholders(arg.Value, game, account, assetsPath, classpath);
                sbArgs.Append(val).Append(" ");
            }

            if (game.JvmArguments.Count == 0 || !sbArgs.ToString().Contains("-cp"))
                sbArgs.Append($"-cp \"{classpath}\" ");

            sbArgs.Append(game.MainClass).Append(" ");
        }

        private void BuildGameArguments(StringBuilder sbArgs, GameInstance game, Account account, Action<string> Log)
        {
            if (!string.IsNullOrEmpty(game.MinecraftArguments))
            {
                string args = ReplacePlaceholders(game.MinecraftArguments, game, account, "", "");
                sbArgs.Append(args);
            }
            else
            {
                foreach (var arg in game.GameArguments)
                {
                    string val = ReplacePlaceholders(arg.Value, game, account, "", "");
                    sbArgs.Append(val).Append(" ");
                }
            }

            if (!string.IsNullOrEmpty(_configService.Settings.GlobalGameArguments))
                sbArgs.Append(_configService.Settings.GlobalGameArguments).Append(" ");
        }

        private Process? StartGameProcess(string javaPath, string arguments, string workingDir, string nativesPath, Action<string> Log)
        {
            var psi = new ProcessStartInfo
            {
                FileName = javaPath,
                Arguments = arguments,
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                var currentPath = psi.EnvironmentVariables["PATH"] ?? Environment.GetEnvironmentVariable("PATH") ?? "";
                if (!currentPath.Split(Path.PathSeparator).Any(p => string.Equals(p, nativesPath, StringComparison.OrdinalIgnoreCase)))
                    psi.EnvironmentVariables["PATH"] = nativesPath + Path.PathSeparator + currentPath;
            }
            catch { }

            var cmd = $"\"{psi.FileName}\" {psi.Arguments}";
            Log($"[CMD] {cmd}");
            File.WriteAllText(Path.Combine(workingDir, "debug_launch_cmd.txt"), cmd);

            var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            if (!process.Start())
            {
                Log("[ERROR] process.Start() returned false");
                _notificationService.Show("启动失败", "无法启动 Java 进程", NotificationType.Error);
                return null;
            }

            switch (_configService.Settings.ProcessPriority)
            {
                case 1: process.PriorityClass = ProcessPriorityClass.AboveNormal; break;
                case 2: process.PriorityClass = ProcessPriorityClass.High; break;
            }

            return process;
        }

        private void WireProcessOutput(Process process, Action<string> Log)
        {
            process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) Log($"[OUT] {e.Data}");
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data)) Log($"[ERR] {e.Data}");
            };
            process.Exited += (s, e) =>
            {
                try { Log($"[EXIT] Code={process.ExitCode}"); } catch { }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        #region Placeholder Replacement

        // Quote values that may contain spaces so they survive command-line parsing.
        // (Templates in version JSONs never wrap these placeholders in quotes themselves.)
        private static string QuoteIfNeeded(string value)
        {
            if (string.IsNullOrEmpty(value) || value.IndexOf(' ') < 0) return value;
            return $"\"{value}\"";
        }

        private string ReplacePlaceholders(string template, GameInstance game, Account account, string assetsPath, string classpath)
        {
            string result = template;

            result = result.Replace("${auth_player_name}", QuoteIfNeeded(account.Username ?? "Player"));
            result = result.Replace("${auth_uuid}", account.Uuid ?? "");
            result = result.Replace("${auth_access_token}", account.AccessToken ?? "");
            string userType = account.Type == AccountType.Microsoft ? "msa" : "mojang";
            result = result.Replace("${user_type}", userType);
            result = result.Replace("${version_name}", QuoteIfNeeded(game.Id));
            result = result.Replace("${version_type}", game.Type);
            result = result.Replace("${game_directory}", QuoteIfNeeded(game.GameDir));
            result = result.Replace("${assets_root}", QuoteIfNeeded(assetsPath));
            result = result.Replace("${assets_index_name}", game.AssetIndexId);
            result = result.Replace("${user_properties}", "{}");
            result = result.Replace("${resolution_width}", _configService.Settings.WindowWidth.ToString());
            result = result.Replace("${resolution_height}", _configService.Settings.WindowHeight.ToString());

            if (!string.IsNullOrEmpty(classpath))
                result = result.Replace("${classpath}", QuoteIfNeeded(classpath));

            result = result.Replace("${natives_directory}", QuoteIfNeeded(Path.Combine(game.GameDir, "natives")));

            return result;
        }

        #endregion

        #region Validate & Repair

        private async Task ValidateAndRepairAsync(GameInstance game, Action<string>? statusCallback)
        {
            string librariesPath = Path.Combine(game.RootPath, "libraries");
            var requests = new List<DownloadRequest>();
            var downloadService = new DownloadService();

            foreach (var lib in game.Libraries)
            {
                if (string.IsNullOrWhiteSpace(lib.Url) || string.IsNullOrWhiteSpace(lib.Checksum)) continue;

                string fullPath = Path.Combine(librariesPath, lib.Path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(fullPath))
                {
                    try
                    {
                        string sha1 = await ComputeSha1Async(fullPath);
                        if (sha1.Equals(lib.Checksum, StringComparison.OrdinalIgnoreCase)) continue;
                    }
                    catch { }
                }

                if (!string.IsNullOrEmpty(lib.Url))
                {
                    requests.Add(new DownloadRequest(lib.Url, fullPath, lib.Checksum));
                }
            }

            string clientJar = Path.Combine(game.RootPath, "versions", game.Id, $"{game.Id}.jar");
            bool clientJarExists = File.Exists(clientJar);
            if (!clientJarExists && !string.IsNullOrEmpty(game.ClientJarUrl))
            {
                requests.Add(new DownloadRequest(game.ClientJarUrl, clientJar, game.ClientJarSha1));
            }

            if (game.LogConfig != null && !string.IsNullOrEmpty(game.LogConfig.File.Url))
            {
                string logConfigPath = Path.Combine(game.RootPath, "assets", "log_configs", game.LogConfig.File.Id);
                if (!File.Exists(logConfigPath) || new FileInfo(logConfigPath).Length != game.LogConfig.File.Size)
                {
                    requests.Add(new DownloadRequest(game.LogConfig.File.Url, logConfigPath, game.LogConfig.File.Sha1));
                }
            }

            if (requests.Count > 0)
            {
                statusCallback?.Invoke($"下载 {requests.Count} 个缺失资源...");
                _notificationService.Show("资源检查", $"检测到 {requests.Count} 个缺失文件", NotificationType.Info);
                try
                {
                    var progress = new Progress<double>(p => { });
                    await downloadService.DownloadBatchAsync(requests, progress);
                    _notificationService.Show("资源检查", "所有资源已下载完成", NotificationType.Success);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Download failed");
                    _notificationService.Show("下载失败", ex.Message, NotificationType.Error);
                }
            }
        }

        private static async Task<string> ComputeSha1Async(string filePath)
        {
            using (var stream = System.IO.File.OpenRead(filePath))
            using (var sha = System.Security.Cryptography.SHA1.Create())
            {
                byte[] hash = await sha.ComputeHashAsync(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        #endregion
    }
}
