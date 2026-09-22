using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TsuruLauncher.Services.Network;
using TsuruLauncher.Utilities;

namespace TsuruLauncher.Services
{
    /// <summary>
    /// 下载并缓存 authlib-injector：外置登录（Yggdrasil）账号必须靠它才能让原版游戏认账。
    /// </summary>
    public static class AuthlibInjectorService
    {
        public const string MetadataUrl = "https://authlib-injector.yushi.moe/artifact/latest.json";

        public static string JarPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TsuruLauncher", "runtime");
                return Path.Combine(dir, "authlib-injector.jar");
            }
        }

        /// <summary>确保本机有 authlib-injector，返回 jar 路径；失败返回 null。</summary>
        public static async Task<string?> EnsureAsync(Action<string>? status = null, CancellationToken ct = default)
        {
            string path = JarPath;
            try
            {
                if (File.Exists(path) && new FileInfo(path).Length > 200 * 1024)
                    return path;

                status?.Invoke("获取 authlib-injector 版本信息...");
                var downloader = new DownloadService();
                string metaJson = await downloader.DownloadStringAsync(MetadataUrl);
                var meta = JObject.Parse(metaJson);
                string url = (string?)meta["download_url"] ?? "";
                string version = (string?)meta["version"] ?? "?";
                if (string.IsNullOrEmpty(url)) throw new Exception("元数据里没有 download_url");

                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                status?.Invoke("下载 authlib-injector " + version + "...");
                await downloader.DownloadFileAsync(url, path, null, null, ct);

                if (!File.Exists(path) || new FileInfo(path).Length < 200 * 1024)
                    throw new Exception("下载到的文件不完整");

                Logger.LogDebug("[Yggdrasil] authlib-injector " + version + " 就绪: " + path);
                return path;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "AuthlibInjectorService.EnsureAsync");
                status?.Invoke("authlib-injector 下载失败: " + ex.Message);
                return null;
            }
        }
    }
}
