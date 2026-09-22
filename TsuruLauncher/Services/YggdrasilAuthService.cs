using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TsuruLauncher.Models;
using TsuruLauncher.Utilities;

namespace TsuruLauncher.Services
{
    public class YggdrasilException : Exception
    {
        public YggdrasilException(string message) : base(message) { }
    }

    /// <summary>
    /// 外置登录（Yggdrasil API / authlib-injector）实现。
    /// 支持 LittleSkin、Blessing Skin 等皮肤站，登录后由 authlib-injector 让游戏识别该账号。
    /// </summary>
    public class YggdrasilAuthService
    {
        public static readonly (string Name, string Url)[] Presets =
        {
            ("LittleSkin", "https://littleskin.cn/api/yggdrasil"),
            ("Blessing Skin (自建)", "https://example.com/api/yggdrasil"),
            ("自定义", "")
        };

        private readonly HttpClient _http;

        public YggdrasilAuthService()
        {
            _http = HttpClientFactory.Client;
        }

        public static string Normalize(string server)
        {
            string url = (server ?? string.Empty).Trim();
            if (url.Length == 0) return url;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
            return url.TrimEnd('/');
        }

        /// <summary>读取 API Root（拿服务器名、签名公钥，也用于校验地址是否正确）。</summary>
        public async Task<string> GetServerNameAsync(string server, CancellationToken ct = default)
        {
            string url = Normalize(server);
            if (url.Length == 0) throw new YggdrasilException("请填写外置登录服务器地址。");

            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
                throw new YggdrasilException("无法连接外置登录服务器 (" + (int)resp.StatusCode + ")，请检查地址是否为 API Root。");

            var json = JObject.Parse(await resp.Content.ReadAsStringAsync(ct));
            var meta = json["meta"] as JObject;
            return (string?)meta?["serverName"] ?? (string?)json["skinDomains"]?[0] ?? "外置登录";
        }

        public async Task<Account> LoginAsync(string server, string username, string password, CancellationToken ct = default)
        {
            string url = Normalize(server);
            if (url.Length == 0) throw new YggdrasilException("请填写外置登录服务器地址。");
            if (string.IsNullOrWhiteSpace(username)) throw new YggdrasilException("请填写邮箱 / 用户名。");
            if (string.IsNullOrEmpty(password)) throw new YggdrasilException("请填写密码。");

            string name = await GetServerNameAsync(url, ct);

            string clientToken = Guid.NewGuid().ToString("N");
            var payload = new JObject
            {
                ["username"] = username.Trim(),
                ["password"] = password,
                ["clientToken"] = clientToken,
                ["requestUser"] = true
            };

            var (ok, body) = await PostAsync(url + "/authserver/authenticate", payload, ct);
            if (!ok)
                throw new YggdrasilException(DescribeError(body) ?? "登录失败，请检查账号密码。");

            var root = JObject.Parse(body);
            string accessToken = (string?)root["accessToken"] ?? "";
            string profileId = (string?)root["selectedProfile"]?["id"] ?? "";
            string profileName = (string?)root["selectedProfile"]?["name"] ?? username.Trim();

            if (string.IsNullOrEmpty(accessToken))
                throw new YggdrasilException("服务器没有返回 accessToken，可能该账号还没有创建角色。");

            var account = new Account
            {
                Username = profileName,
                Uuid = profileId,
                AccessToken = accessToken,
                ClientToken = clientToken,
                YggdrasilServer = url,
                Type = AccountType.Yggdrasil,
                ExpiryTime = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds()
            };

            Logger.LogDebug("[Yggdrasil] 登录成功 server=" + url + " user=" + profileName + " (" + name + ")");
            return account;
        }

        /// <summary>校验令牌是否还有效。</summary>
        public async Task<bool> ValidateAsync(Account account, CancellationToken ct = default)
        {
            if (account.Type != AccountType.Yggdrasil || string.IsNullOrEmpty(account.YggdrasilServer)) return false;
            try
            {
                var payload = new JObject
                {
                    ["accessToken"] = account.AccessToken,
                    ["clientToken"] = account.ClientToken
                };
                var (ok, _) = await PostAsync(account.YggdrasilServer + "/authserver/validate", payload, ct);
                return ok;
            }
            catch { return false; }
        }

        /// <summary>刷新令牌（部分服务器要求定期刷新，否则游戏内会显示未登录）。</summary>
        public async Task<bool> RefreshAsync(Account account, CancellationToken ct = default)
        {
            if (account.Type != AccountType.Yggdrasil || string.IsNullOrEmpty(account.YggdrasilServer)) return false;
            try
            {
                var payload = new JObject
                {
                    ["accessToken"] = account.AccessToken,
                    ["clientToken"] = account.ClientToken,
                    ["requestUser"] = true
                };
                var (ok, body) = await PostAsync(account.YggdrasilServer + "/authserver/refresh", payload, ct);
                if (!ok) return false;

                var root = JObject.Parse(body);
                string token = (string?)root["accessToken"] ?? "";
                if (string.IsNullOrEmpty(token)) return false;

                account.AccessToken = token;
                if (root["selectedProfile"] != null)
                {
                    account.Uuid = (string?)root["selectedProfile"]?["id"] ?? account.Uuid;
                    account.Username = (string?)root["selectedProfile"]?["name"] ?? account.Username;
                }
                account.ExpiryTime = DateTimeOffset.UtcNow.AddHours(12).ToUnixTimeSeconds();
                return true;
            }
            catch { return false; }
        }

        private async Task<(bool Ok, string Body)> PostAsync(string url, JObject payload, CancellationToken ct)
        {
            using var content = new StringContent(payload.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
            using var resp = await _http.PostAsync(url, content, ct);
            string body = await resp.Content.ReadAsStringAsync(ct);
            return (resp.IsSuccessStatusCode, body);
        }

        public static string? DescribeError(string body)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;
            try
            {
                var json = JObject.Parse(body);
                string? message = (string?)json["errorMessage"] ?? (string?)json["error"];
                if (!string.IsNullOrEmpty(message)) return message;
            }
            catch { }
            return null;
        }

        /// <summary>启动游戏时要给 JVM 加的参数（由 authlib-injector 接管登录）。</summary>
        public static string BuildAuthlibArguments(string jarPath, string server)
        {
            string url = Normalize(server);
            var sb = new StringBuilder();
            sb.Append("-javaagent:\"").Append(jarPath).Append("\"=\"").Append(url).Append("\" ");
            sb.Append("-Dminecraft.api.auth.host=").Append(url).Append("/authserver ");
            sb.Append("-Dminecraft.api.account.host=").Append(url).Append("/api ");
            sb.Append("-Dminecraft.api.session.host=").Append(url).Append("/sessionserver ");
            sb.Append("-Dminecraft.api.services.host=").Append(url).Append("/api ");
            return sb.ToString();
        }
    }
}
