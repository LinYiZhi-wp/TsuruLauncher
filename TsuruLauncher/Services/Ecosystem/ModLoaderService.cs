using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TsuruLauncher.Services.Network;

namespace TsuruLauncher.Services.Ecosystem
{
    public class ModLoaderService
    {
        private readonly DownloadService _downloadService;
        private const string FabricMetaUrl = "https://meta.fabricmc.net/v2";

        public ModLoaderService()
        {
            _downloadService = new DownloadService();
        }

        public async Task<JObject> InstallFabricAsync(string mcVersion, string loaderVersion, string dotMinecraftPath, IProgress<double>? progress = null, IProgress<string>? status = null)
        {
            string versionId = $"{mcVersion}-fabric-{loaderVersion}";
            string versionDir = Path.Combine(dotMinecraftPath, "versions", versionId);
            string jsonPath = Path.Combine(versionDir, $"{versionId}.json");

            string url = $"{FabricMetaUrl}/versions/loader/{mcVersion}/{loaderVersion}/profile/json";
            status?.Report($"Fetching Fabric metadata...");

            string jsonContent = await _downloadService.DownloadStringAsync(url);
            var json = JObject.Parse(jsonContent);

            if (!Directory.Exists(versionDir)) Directory.CreateDirectory(versionDir);

            json["id"] = versionId;
            File.WriteAllText(jsonPath, json.ToString());

            var libraries = json["libraries"] as JArray;
            if (libraries != null)
            {
                var downloads = new List<DownloadRequest>();

                foreach (var lib in libraries)
                {
                    string name = lib["name"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(name)) continue;

                    var parts = name.Split(':');
                    if (parts.Length < 3) continue;

                    string group = parts[0].Replace('.', '/');
                    string artifact = parts[1];
                    string version = parts[2];
                    var downloadsToken = lib["downloads"];

                    // 1) Main artifact — use explicit downloads.artifact metadata when present,
                    // otherwise fall back to the maven layout. Mojang-hosted libraries carry no
                    // "url" field, so default to libraries.minecraft.net (NOT maven.fabricmc.net).
                    var artifactToken = downloadsToken?["artifact"];
                    if (artifactToken != null && !string.IsNullOrEmpty(artifactToken["path"]?.ToString()))
                    {
                        string p = artifactToken["path"].ToString();
                        string artifactUrl = artifactToken["url"]?.ToString() ?? "";
                        if (string.IsNullOrEmpty(artifactUrl)) artifactUrl = (lib["url"]?.ToString() ?? "https://libraries.minecraft.net/") + p;
                        string destPath = Path.Combine(dotMinecraftPath, "libraries", p);
                        if (!File.Exists(destPath))
                        {
                            downloads.Add(new DownloadRequest
                            {
                                Url = artifactUrl,
                                DestinationPath = destPath,
                                Sha1 = artifactToken["sha1"]?.ToString()
                            });
                        }
                    }
                    else if (parts.Length == 3)
                    {
                        string path = $"{group}/{artifact}/{version}/{artifact}-{version}.jar";
                        string urlBase = lib["url"]?.ToString() ?? "https://libraries.minecraft.net/";
                        string destPath = Path.Combine(dotMinecraftPath, "libraries", path);
                        if (!File.Exists(destPath))
                        {
                            downloads.Add(new DownloadRequest
                            {
                                Url = $"{urlBase}{path}",
                                DestinationPath = destPath
                            });
                        }
                    }

                    // 2) Natives classifier (windows) — required for LWJGL at launch.
                    string? classifier = null;
                    var natives = lib["natives"];
                    if (natives != null)
                    {
                        classifier = natives["windows"]?.ToString();
                        if (!string.IsNullOrEmpty(classifier))
                            classifier = classifier.Replace("$" + "{arch}", IntPtr.Size == 8 ? "64" : "32");
                    }
                    if (string.IsNullOrEmpty(classifier) && parts.Length > 3 &&
                        parts[3].Contains("natives", StringComparison.OrdinalIgnoreCase))
                        classifier = parts[3];

                    if (!string.IsNullOrEmpty(classifier))
                    {
                        var classifiers = downloadsToken?["classifiers"] as JObject;
                        var classToken = classifiers?[classifier];
                        if (classToken != null && !string.IsNullOrEmpty(classToken["path"]?.ToString()))
                        {
                            string p = classToken["path"].ToString();
                            string classifierUrl = classToken["url"]?.ToString() ?? "";
                            if (string.IsNullOrEmpty(classifierUrl)) classifierUrl = (lib["url"]?.ToString() ?? "https://libraries.minecraft.net/") + p;
                            string destPath = Path.Combine(dotMinecraftPath, "libraries", p);
                            if (!File.Exists(destPath))
                            {
                                downloads.Add(new DownloadRequest
                                {
                                    Url = classifierUrl,
                                    DestinationPath = destPath,
                                    Sha1 = classToken["sha1"]?.ToString()
                                });
                            }
                        }
                        else if (parts.Length > 3 && classifier == parts[3])
                        {
                            // No downloads metadata: guess the classifier jar path
                            string path = $"{group}/{artifact}/{version}/{artifact}-{version}-{classifier}.jar";
                            string guessedUrl = (lib["url"]?.ToString() ?? "https://libraries.minecraft.net/") + path;
                            string destPath = Path.Combine(dotMinecraftPath, "libraries", path);
                            if (!File.Exists(destPath))
                            {
                                downloads.Add(new DownloadRequest
                                {
                                    Url = guessedUrl,
                                    DestinationPath = destPath
                                });
                            }
                        }
                    }
                }

                if (downloads.Any())
                {
                    status?.Report($"Downloading {downloads.Count} libraries...");
                    await _downloadService.DownloadBatchAsync(downloads, progress ?? new Progress<double>());
                }
            }

            return json;
        }

        private const string BMCLAPI_FORGE = "https://bmclapi2.bangbang93.com";

        public async Task InstallForgeAsync(string mcVersion, string forgeVersion, string dotMinecraftPath, IProgress<double>? progress = null, IProgress<string>? status = null)
        {
            status?.Report("正在获取Forge安装信息...");

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

                var versionsUrl = $"{BMCLAPI_FORGE}/forge/minecraft/{mcVersion}";
                var versionsJson = await client.GetStringAsync(versionsUrl);
                var versions = JArray.Parse(versionsJson);

                JToken? forgeData = null;
                foreach (var v in versions)
                {
                    if (v["version"]?.ToString() == forgeVersion)
                    {
                        forgeData = v;
                        break;
                    }
                }

                if (forgeData == null)
                {
                    foreach (var v in versions.OrderByDescending(x =>
                    {
                        int.TryParse(x["build"]?.ToString(), out int b);
                        return b;
                    }))
                    {
                        string ver = v["version"]?.ToString() ?? "";
                        if (ver.Contains(forgeVersion) || forgeVersion.Contains(ver.Split(' ').FirstOrDefault() ?? ""))
                        {
                            forgeData = v;
                            break;
                        }
                    }
                }

                if (forgeData == null)
                    throw new Exception($"找不到Forge版本 {forgeVersion}");

                // forgeVersionId is the full version string from BMCLAPI, e.g. "1.20.1-47.2.0"
                string forgeVersionId = forgeData["version"]?.ToString() ?? forgeVersion;

                // Extract the forge build number from the version ID (e.g. "47.2.0" from "1.20.1-47.2.0")
                string forgeBuild = forgeVersionId;
                int dashIdx = forgeVersionId.IndexOf('-');
                if (dashIdx > 0)
                    forgeBuild = forgeVersionId.Substring(dashIdx + 1);

                status?.Report($"正在下载Forge {forgeVersionId}...");

                // Some BMCLAPI entries report the bare build ("47.2.0") instead of the
                // full maven coordinate ("1.20.1-47.2.0") — cover both layouts.
                string fullForgeId = forgeVersionId.Contains(mcVersion + "-")
                    ? forgeVersionId
                    : $"{mcVersion}-{forgeVersionId}";
                var installerUrls = new[]
                {
                    $"{BMCLAPI_FORGE}/maven/net/minecraftforge/forge/{fullForgeId}/forge-{fullForgeId}-installer.jar",
                    $"{BMCLAPI_FORGE}/maven/net/minecraftforge/forge/{forgeVersionId}/forge-{forgeVersionId}-installer.jar",
                    $"https://maven.minecraftforge.net/net/minecraftforge/forge/{fullForgeId}/forge-{fullForgeId}-installer.jar"
                };
                string installerPath = Path.Combine(dotMinecraftPath, "temp_forge_installer.jar");

                await DownloadWithFallbackAsync(installerUrls, installerPath);

                status?.Report("正在运行Forge安装程序...");

                int requiredJava = GetForgeJavaRequirement(mcVersion);

                // Pick the argument style for this MC version, and if the installer
                // rejects it (UnrecognizedOptionException), retry with the other style.
                bool legacy = IsLegacyForgeInstaller(mcVersion);
                string? installError = await RunForgeInstallerOnceAsync(installerPath, dotMinecraftPath, requiredJava, legacy, progress, status);
                if (installError != null &&
                    (installError.Contains("UnrecognizedOption", StringComparison.OrdinalIgnoreCase) ||
                     installError.Contains("not a recognized option", StringComparison.OrdinalIgnoreCase)))
                {
                    status?.Report("安装器参数格式不兼容，自动切换重试...");
                    installError = await RunForgeInstallerOnceAsync(installerPath, dotMinecraftPath, requiredJava, !legacy, progress, status);
                }
                if (installError != null)
                    throw new Exception(installError);

                // The Forge installer creates the version folder as {mcVersion}-forge-{forgeBuild}
                // e.g. "1.20.1-forge-47.2.0", but OLD installers (≤1.12.2) use
                // "{mcVersion}-forge{build}" without the dash, e.g. "1.8.9-forge11.15.1.2318".
                string forgeVersionDir = Path.Combine(dotMinecraftPath, "versions", $"{mcVersion}-forge-{forgeBuild}");
                string forgeJsonPath = Path.Combine(forgeVersionDir, $"{mcVersion}-forge-{forgeBuild}.json");

                // Fallback 1: original forgeVersionId format (for older Forge)
                if (!File.Exists(forgeJsonPath))
                {
                    forgeVersionDir = Path.Combine(dotMinecraftPath, "versions", $"{mcVersion}-forge-{forgeVersionId}");
                    forgeJsonPath = Path.Combine(forgeVersionDir, $"{mcVersion}-forge-{forgeVersionId}.json");
                }

                // Fallback 2: scan the versions folder for any "{mc}-forge*" directory
                // (covers "1.8.9-forge11.15.1.2318" and other legacy layouts)
                if (!File.Exists(forgeJsonPath))
                {
                    string versionsDir = Path.Combine(dotMinecraftPath, "versions");
                    if (Directory.Exists(versionsDir))
                    {
                        string? legacyName = Directory.GetDirectories(versionsDir)
                            .Select(Path.GetFileName)
                            .FirstOrDefault(n => n != null &&
                                n.StartsWith($"{mcVersion}-forge", StringComparison.OrdinalIgnoreCase) &&
                                File.Exists(Path.Combine(versionsDir, n, $"{n}.json")));
                        if (legacyName != null)
                        {
                            forgeVersionDir = Path.Combine(versionsDir, legacyName);
                            forgeJsonPath = Path.Combine(forgeVersionDir, $"{legacyName}.json");
                        }
                    }
                }

                if (File.Exists(forgeJsonPath))
                {
                    var forgeJson = JObject.Parse(File.ReadAllText(forgeJsonPath));
                    // Keep the JSON id in sync with the folder name — GameService
                    // resolves jars by id, so a mismatched id breaks launching.
                    string dirName = Path.GetFileName(forgeVersionDir);
                    if (forgeJson["id"]?.ToString() != dirName)
                        forgeJson["id"] = dirName;
                    File.WriteAllText(forgeJsonPath, forgeJson.ToString());
                }
                else
                {
                    // The installer reported success but produced nothing — that is a failure
                    // the user must see, not a silent pass.
                    string versionsDir = Path.Combine(dotMinecraftPath, "versions");
                    string found = string.Empty;
                    if (Directory.Exists(versionsDir))
                    {
                        found = string.Join(", ", Directory.GetDirectories(versionsDir)
                            .Select(Path.GetFileName)
                            .Where(n => n != null && n.Contains("forge", StringComparison.OrdinalIgnoreCase)));
                    }
                    throw new Exception($"Forge 安装程序已运行但未找到生成的版本 (查找: {Path.GetFileName(forgeJsonPath)})。\n已发现的 Forge 版本目录: {(string.IsNullOrEmpty(found) ? "无" : found)}");
                }

                status?.Report("Forge安装完成！");
                progress?.Report(1.0);
            }
            catch (Exception ex)
            {
                status?.Report($"Forge安装失败: {ex.Message}");
                // Clean up a half-downloaded installer on failure
                try
                {
                    string leftover = Path.Combine(dotMinecraftPath, "temp_forge_installer.jar");
                    if (File.Exists(leftover)) File.Delete(leftover);
                }
                catch { }
                throw;
            }
        }

        /// <summary>
        /// Installs OptiFine by downloading the installer jar from BMCLAPI and
        /// running it in silent mode (creates versions/{mc}-OptiFine).
        /// optifineVersion is expected in "type_patch" form, e.g. "HD_U_I6".
        /// </summary>
        public async Task InstallOptiFineAsync(string mcVersion, string optifineVersion, string dotMinecraftPath, IProgress<double>? progress = null, IProgress<string>? status = null)
        {
            if (string.IsNullOrEmpty(optifineVersion))
                throw new Exception("OptiFine 版本为空");

            var versionParts = optifineVersion.Split('_');
            if (versionParts.Length < 2)
                throw new Exception($"无效的 OptiFine 版本: {optifineVersion}");
            string type = string.Join("_", versionParts.Take(versionParts.Length - 1));
            string patch = versionParts[versionParts.Length - 1];

            // Try the known BMCLAPI download layouts
            var candidates = new[]
            {
                $"{BMCLAPI_FORGE}/optifine/{mcVersion}/{type}/{patch}",
                $"{BMCLAPI_FORGE}/optifine/{mcVersion}/{type}_{patch}",
                $"{BMCLAPI_FORGE}/optifine/{mcVersion}/download?type={type}&patch={patch}"
            };

            string? installerPath = null;
            foreach (var url in candidates)
            {
                string candidate = Path.Combine(Path.GetTempPath(), $"optifine_{Guid.NewGuid():N}.jar");
                try
                {
                    status?.Report($"正在下载 OptiFine {optifineVersion}...");
                    await _downloadService.DownloadFileAsync(url, candidate);
                    installerPath = candidate;
                    break;
                }
                catch
                {
                    try { if (File.Exists(candidate)) File.Delete(candidate); } catch { }
                }
            }

            if (installerPath == null)
                throw new Exception($"无法从 BMCLAPI 下载 OptiFine {optifineVersion}，请检查版本是否正确");

            try
            {
                status?.Report("正在运行 OptiFine 安装程序...");

                var javaPath = FindJavaPath(GetForgeJavaRequirement(mcVersion));
                if (string.IsNullOrEmpty(javaPath))
                    throw new Exception("未找到Java运行环境");

                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = javaPath,
                        Arguments = $"-jar \"{installerPath}\" --installDir \"{dotMinecraftPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = Path.GetTempPath()
                    }
                };

                var outputBuilder = new System.Text.StringBuilder();
                var errorBuilder = new System.Text.StringBuilder();
                process.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) outputBuilder.AppendLine(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) errorBuilder.AppendLine(e.Data); };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                while (!process.HasExited)
                {
                    await Task.Delay(500);
                    progress?.Report(0.5);
                }
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    var err = string.IsNullOrEmpty(errorBuilder.ToString()) ? outputBuilder.ToString() : errorBuilder.ToString();
                    throw new Exception($"OptiFine 安装程序返回错误码: {process.ExitCode}\n{err}");
                }

                // The installer creates versions/{mcVersion}-OptiFine/{mcVersion}-OptiFine.json
                string versionsDir = Path.Combine(dotMinecraftPath, "versions");
                string? optifineDir = null;
                if (Directory.Exists(versionsDir))
                {
                    optifineDir = Directory.GetDirectories(versionsDir)
                        .FirstOrDefault(d => Path.GetFileName(d).StartsWith($"{mcVersion}-OptiFine", StringComparison.OrdinalIgnoreCase))
                        ?? Directory.GetDirectories(versionsDir)
                           .FirstOrDefault(d => Path.GetFileName(d).Contains("OptiFine", StringComparison.OrdinalIgnoreCase));
                }

                if (optifineDir == null)
                    throw new Exception("OptiFine 安装完成但未找到生成的版本目录");

                string dirName = Path.GetFileName(optifineDir);
                string jsonPath = Path.Combine(optifineDir, $"{dirName}.json");
                if (File.Exists(jsonPath))
                {
                    var json = JObject.Parse(File.ReadAllText(jsonPath));
                    json["id"] = dirName;
                    File.WriteAllText(jsonPath, json.ToString());
                }

                status?.Report("OptiFine安装完成！");
                progress?.Report(1.0);
            }
            finally
            {
                try { if (File.Exists(installerPath)) File.Delete(installerPath); } catch { }
            }
        }

        /// <summary>
        /// 1.12.2 and older Forge installers use the legacy SimpleInstaller which only
        /// accepts the install folder as a positional argument after --installClient.
        /// 1.13+ installers use --installDir instead.
        /// </summary>
        private static bool IsLegacyForgeInstaller(string mcVersion)
        {
            var parts = mcVersion.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[1], out int minor))
                return minor <= 12;
            return true;
        }

        /// <summary>
        /// Runs the Forge installer once with the given argument style.
        /// Returns the error text on failure, or null on success.
        /// </summary>
        private async Task<string?> RunForgeInstallerOnceAsync(string installerPath, string dotMinecraftPath, int requiredJava, bool legacyArgs, IProgress<double>? progress, IProgress<string>? status)
        {
            var javaPath = FindJavaPath(requiredJava);
            if (string.IsNullOrEmpty(javaPath))
                throw new Exception("未找到Java运行环境");

            var tempDir = Path.Combine(Path.GetTempPath(), $"forge_install_{Guid.NewGuid():N}");
            Directory.CreateDirectory(tempDir);

            try
            {
                // Legacy: --installClient "path" (positional).
                // Modern: --installClient --installDir "path".
                string args = legacyArgs
                    ? $"-jar \"{installerPath}\" --installClient \"{dotMinecraftPath}\""
                    : $"-jar \"{installerPath}\" --installClient --installDir \"{dotMinecraftPath}\"";

                using var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = javaPath,
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                        WorkingDirectory = tempDir
                    }
                };

                var outputBuilder = new System.Text.StringBuilder();
                var errorBuilder = new System.Text.StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        outputBuilder.AppendLine(e.Data);
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                        errorBuilder.AppendLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                while (!process.HasExited)
                {
                    await Task.Delay(500);
                    progress?.Report(0.5);
                }

                await process.WaitForExitAsync();

                var output = outputBuilder.ToString();
                var error = errorBuilder.ToString();
                Utilities.Logger.LogInfo($"[ForgeInstaller] Java={javaPath} Args={args} ExitCode={process.ExitCode}");
                if (!string.IsNullOrEmpty(output)) Utilities.Logger.LogInfo($"[ForgeInstaller] Output: {output.Trim()}");
                if (!string.IsNullOrEmpty(error)) Utilities.Logger.LogError(new Exception(error), "ForgeInstaller");

                if (process.ExitCode != 0)
                {
                    var errorMsg = string.IsNullOrEmpty(error) ? output : error;
                    return $"Forge安装程序返回错误码: {process.ExitCode}\n{errorMsg}";
                }

                progress?.Report(1.0);
                return null;
            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        /// <summary>
        /// Picks a Java for the installer: prefer the required major version
        /// (8 for ≤1.16, 17 for 1.17+, 21 for 1.20.5+/1.21+), then fall back
        /// to JAVA_HOME / PATH / common install locations.
        /// </summary>
        private string? FindJavaPath(int requiredVersion = 0)
        {
            if (requiredVersion > 0)
            {
                try
                {
                    var best = new JavaService().AutoDetectBestJava(requiredVersion);
                    if (!string.IsNullOrEmpty(best) && File.Exists(best)) return best;
                }
                catch { }
            }

            var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
            if (!string.IsNullOrEmpty(javaHome))
            {
                var javapath = Path.Combine(javaHome, "bin", "java.exe");
                if (File.Exists(javapath)) return javapath;
            }

            var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? Array.Empty<string>();
            foreach (var path in paths)
            {
                var javapath = Path.Combine(path, "java.exe");
                if (File.Exists(javapath)) return javapath;
            }

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var javaDir = Path.Combine(programFiles, "Java");
            if (Directory.Exists(javaDir))
            {
                foreach (var dir in Directory.GetDirectories(javaDir))
                {
                    var javapath = Path.Combine(dir, "bin", "java.exe");
                    if (File.Exists(javapath)) return javapath;
                }
            }

            return null;
        }

        private static int GetForgeJavaRequirement(string mcVersion)
        {
            var parts = mcVersion.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[1], out int minor))
            {
                if (minor >= 21) return 21;
                if (minor == 20)
                {
                    if (parts.Length >= 3 && int.TryParse(parts[2], out int patch) && patch >= 5)
                        return 21;
                    return 17;
                }
                if (minor >= 17) return 17;
            }
            return 8;
        }

        private async Task DownloadWithFallbackAsync(IEnumerable<string> urls, string destPath)
        {
            Exception? last = null;
            foreach (var url in urls)
            {
                try
                {
                    await _downloadService.DownloadFileAsync(url, destPath);
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
                }
            }
            throw last ?? new Exception("下载失败");
        }

    }
}