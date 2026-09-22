using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TsuruLauncher.Services
{
    /// <summary>
    /// Makes sure a suitable Java runtime exists: reuses a local installation when
    /// one matches, otherwise downloads a JRE from the Adoptium (Eclipse Temurin)
    /// public API into the launcher's own runtime folder.
    /// </summary>
    public static class JavaRuntimeService
    {
        /// <summary>Where launcher-managed runtimes live.</summary>
        public static string RuntimeRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "TsuruLauncher", "runtime");

        private static readonly HttpClient Http = new(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            ConnectTimeout = TimeSpan.FromSeconds(30)
        });

        /// <summary>
        /// Returns a java.exe path able to run <paramref name="majorVersion"/>,
        /// downloading it when necessary. Throws with a readable message on failure.
        /// </summary>
        public static async Task<string> EnsureAsync(
            int majorVersion,
            IProgress<double>? progress = null,
            IProgress<string>? status = null,
            CancellationToken ct = default)
        {
            // 1) an existing local installation wins
            status?.Report($"正在查找 Java {majorVersion}...");
            var local = new JavaService().AutoDetectBestJava(majorVersion);
            if (!string.IsNullOrEmpty(local)) return local;

            // 2) previously downloaded runtime
            string targetDir = Path.Combine(RuntimeRoot, $"jre-{majorVersion}");
            string? ready = FindJavaExe(targetDir);
            if (ready != null) return ready;

            // 3) download from Adoptium
            status?.Report($"未找到 Java {majorVersion}，正在从 Adoptium 获取...");
            var package = await ResolvePackageAsync(majorVersion, ct);

            Directory.CreateDirectory(RuntimeRoot);
            string zipPath = Path.Combine(Path.GetTempPath(), $"tsuru-jre-{majorVersion}-{Guid.NewGuid():N}.zip");

            try
            {
                long total = package.Size;
                long received = 0;
                var byteProgress = new Progress<long>(bytes =>
                {
                    received += bytes;
                    if (total > 0)
                        progress?.Report(Math.Min(1.0, (double)received / total));
                });

                status?.Report($"正在下载 Java {majorVersion} ({FormatSize(total)})...");
                var downloader = new Network.DownloadService(maxConcurrency: 8);
                await downloader.DownloadFileAsync(package.Url, zipPath, null, byteProgress, ct);

                status?.Report("正在解压运行时...");
                progress?.Report(0.97);
                await Task.Run(() => ExtractRuntime(zipPath, targetDir), ct);

                var exe = FindJavaExe(targetDir);
                if (exe == null)
                    throw new Exception("运行时解压完成，但未找到 java.exe");

                progress?.Report(1.0);
                status?.Report($"Java {majorVersion} 已就绪");
                return exe;
            }
            finally
            {
                try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
            }
        }

        private sealed record PackageInfo(string Url, long Size, string FileName);

        private static async Task<PackageInfo> ResolvePackageAsync(int majorVersion, CancellationToken ct)
        {
            // Adoptium v3: latest JRE for the requested feature release
            string api = "https://api.adoptium.net/v3/assets/latest/" + majorVersion +
                         "/hotspot?os=windows&architecture=x64&image_type=jre";

            string json;
            try
            {
                using var response = await Http.GetAsync(api, ct);
                response.EnsureSuccessStatusCode();
                json = await response.Content.ReadAsStringAsync(ct);
            }
            catch (Exception ex)
            {
                throw new Exception($"无法连接 Java 下载服务: {ex.Message}");
            }

            var array = JArray.Parse(json);
            var first = array.FirstOrDefault();
            var pkg = first?["binary"]?["package"];
            string? link = pkg?["link"]?.ToString();
            if (string.IsNullOrEmpty(link))
                throw new Exception($"Adoptium 上没有找到 Java {majorVersion} 的 Windows x64 JRE");

            return new PackageInfo(
                link,
                (long)(pkg?["size"] ?? 0),
                pkg?["name"]?.ToString() ?? $"jre-{majorVersion}.zip");
        }

        /// <summary>Extracts the archive and flattens the single top-level folder.</summary>
        private static void ExtractRuntime(string zipPath, string targetDir)
        {
            string temp = Path.Combine(Path.GetTempPath(), "tsuru-jre-extract-" + Guid.NewGuid().ToString("N"));
            try
            {
                ZipFile.ExtractToDirectory(zipPath, temp);

                // the archive contains one root folder (e.g. jdk-21.0.5+11-jre)
                string root = Directory.GetDirectories(temp).FirstOrDefault() ?? temp;

                if (Directory.Exists(targetDir))
                    Directory.Delete(targetDir, true);
                Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);
                Directory.Move(root, targetDir);
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { }
            }
        }

        /// <summary>Looks for java.exe / javaw.exe inside a runtime folder.</summary>
        public static string? FindJavaExe(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return null;
                foreach (var name in new[] { "javaw.exe", "java.exe" })
                {
                    var hit = Directory.GetFiles(dir, name, SearchOption.AllDirectories).FirstOrDefault();
                    if (hit != null) return hit;
                }
            }
            catch { }
            return null;
        }

        private static string FormatSize(long bytes) => bytes switch
        {
            >= 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024 / 1024:F1} GB",
            >= 1024 * 1024 => $"{bytes / 1024.0 / 1024:F0} MB",
            _ => $"{bytes / 1024} KB"
        };
    }
}
