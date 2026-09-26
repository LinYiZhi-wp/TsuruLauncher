using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using TsuruLauncher.Models;
using System.Diagnostics;
using System.Windows;

namespace TsuruLauncher.Services
{
    public class AuthenticationService
    {
        private const string ClientId = "00000000402b5328"; // Azure Client ID (commonly used for Minecraft Launchers)
        private const string RedirectUri = "https://login.live.com/oauth20_desktop.srf";

        // scope 用官方文档里的小写写法 "XboxLive.signin"。
        // （实测旧的 login.live.com 端点对大小写不敏感，写 "XboxLive.Signin" 也能过；
        //   但微软 identity platform 那边是大小写敏感的，所以统一按官方写法来。）
        private const string Scope = "XboxLive.signin offline_access";

        private readonly HttpClient _httpClient;

        public AuthenticationService()
        {
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// POST 一个 JSON 请求并返回响应体。
        ///
        /// ⚠ 失败时**必须把响应体带进异常** —— Microsoft / Xbox 的 400 响应体里
        ///   通常写着具体原因（AADSTS70011 scope 无效、XSTS 2148916233 账号没买游戏…），
        ///   只抛 "400 (Bad Request)" 等于什么都没说，根本没法排查。
        /// </summary>
        private static async Task<JObject> PostJsonAsync(HttpClient http, string url, HttpContent content, string step)
        {
            var response = await http.PostAsync(url, content);
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"{step}失败（HTTP {(int)response.StatusCode}）\n{Truncate(body, 400)}");

            return JObject.Parse(body);
        }

        private static string Truncate(string s, int max)
            => string.IsNullOrEmpty(s) ? "(空响应)" : (s.Length <= max ? s : s.Substring(0, max) + "…");

        // --- Offline Login ---
        public Account LoginOffline(string username)
        {
            return new Account
            {
                Username = username,
                Uuid = GenerateOfflineUuid(username),
                AccessToken = Guid.NewGuid().ToString("N"),
                Type = AccountType.Offline
            };
        }

        private string GenerateOfflineUuid(string username)
        {
            string input = "OfflinePlayer:" + username;
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                hash[6] = (byte)((hash[6] & 0x0f) | 0x30);
                hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
                return new Guid(hash).ToString();
            }
        }

        // --- Microsoft Login ---
        
        public async Task<Account> LoginMicrosoftAsync()
        {
            // 1. Get Authorization Code
            string authCode = await GetAuthorizationCodeAsync();

            // 2. Get Microsoft Access Token
            var msTokenData = await GetMicrosoftAccessTokenAsync(authCode);
            string msAccessToken = msTokenData.AccessToken;
            string refreshToken = msTokenData.RefreshToken;

            // 3. Authenticate with Xbox Live
            string xblToken = await AuthenticateXboxLiveAsync(msAccessToken);

            // 4. Authenticate with XSTS
            var xstsData = await AuthenticateXstsAsync(xblToken);
            string xstsToken = xstsData.Token;
            string userHash = xstsData.UserHash;

            // 5. Authenticate with Minecraft
            var mcTokenData = await AuthenticateMinecraftAsync(userHash, xstsToken);
            string mcAccessToken = mcTokenData.AccessToken;
            // mcTokenData also has expires_in

            // 6. Get Game Profile
            var profile = await GetMinecraftProfileAsync(mcAccessToken);

            return new Account
            {
                Username = profile.Name,
                Uuid = profile.Id,
                AccessToken = mcAccessToken,
                RefreshToken = refreshToken,
                MinecraftAccessToken = mcAccessToken,
                ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + mcTokenData.ExpiresIn,
                Type = AccountType.Microsoft
            };
        }

        private Task<string> GetAuthorizationCodeAsync()
        {
            string url = $"https://login.live.com/oauth20_authorize.srf?client_id={ClientId}&response_type=code&redirect_uri={RedirectUri}&scope={Scope}";
            
            return Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new Views.Dialogs.MicrosoftLoginDialog(url, RedirectUri)
                {
                    Owner = Application.Current.MainWindow
                };

                if (dialog.ShowDialog() == true)
                {
                    return dialog.AuthorizationCode!;
                }
                
                throw new OperationCanceledException("Login cancelled by user.");
            }).Task;
        }

        private async Task<(string AccessToken, string RefreshToken)> GetMicrosoftAccessTokenAsync(string code)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", ClientId),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("redirect_uri", RedirectUri),
                new KeyValuePair<string, string>("scope", Scope)
            });

            var json = await PostJsonAsync(_httpClient, "https://login.live.com/oauth20_token.srf",
                content, "换取 Microsoft 令牌");

            return (json["access_token"]?.ToString() ?? "", json["refresh_token"]?.ToString() ?? "");
        }

        private async Task<string> AuthenticateXboxLiveAsync(string msAccessToken)
        {
            var payload = new
            {
                Properties = new
                {
                    AuthMethod = "RPS",
                    SiteName = "user.auth.xboxlive.com",
                    RpsTicket = $"d={msAccessToken}"
                },
                RelyingParty = "http://auth.xboxlive.com",
                TokenType = "JWT"
            };

            var content = new StringContent(JObject.FromObject(payload).ToString(), Encoding.UTF8, "application/json");
            var json = await PostJsonAsync(_httpClient, "https://user.auth.xboxlive.com/user/authenticate",
                content, "Xbox Live 认证");

            return json["Token"]?.ToString() ?? throw new Exception("Xbox Live 认证失败：响应里没有 Token");
        }

        private async Task<(string Token, string UserHash)> AuthenticateXstsAsync(string xblToken)
        {
            var payload = new
            {
                Properties = new
                {
                    SandboxId = "RETAIL",
                    UserTokens = new[] { xblToken }
                },
                RelyingParty = "rp://api.minecraftservices.com/",
                TokenType = "JWT"
            };

            var content = new StringContent(JObject.FromObject(payload).ToString(), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://xsts.auth.xboxlive.com/xsts/authorize", content);
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // XSTS 失败时响应体里带 XErr 码，含义都是固定的，直接翻译给用户看，
                // 否则只会看到一句 "400 Bad Request"，完全不知道是账号问题还是网络问题。
                long xerr = 0;
                try { xerr = (long)(JObject.Parse(body)["XErr"] ?? 0); } catch { }

                string hint = xerr switch
                {
                    2148916227 => "该账号已被 Xbox Live 封禁。",
                    2148916233 => "该 Microsoft 账号还没有 Xbox 档案。请先到 xbox.com 登录一次创建档案。",
                    2148916235 => "Xbox Live 在你所在的国家/地区不可用。",
                    2148916236 or 2148916237 => "该账号需要完成成人验证（地区限制）。",
                    2148916238 => "这是未成年账号，需要先加入家庭组（由成年人添加）。",
                    _ => string.IsNullOrWhiteSpace(body) ? "(空响应)" : Truncate(body, 400)
                };

                throw new Exception($"XSTS 授权失败（HTTP {(int)response.StatusCode}）\n{hint}");
            }

            var json = JObject.Parse(body);
            
            string token = json["Token"]?.ToString() ?? throw new Exception("XSTS Auth Failed: No Token");
            string uhs = json["DisplayClaims"]?["xui"]?[0]?["uhs"]?.ToString() ?? throw new Exception("XSTS Auth Failed: No UHS");
            
            return (token, uhs);
        }

        private async Task<(string AccessToken, long ExpiresIn)> AuthenticateMinecraftAsync(string userHash, string xstsToken)
        {
            var payload = new
            {
                identityToken = $"XBL3.0 x={userHash};{xstsToken}"
            };

            var content = new StringContent(JObject.FromObject(payload).ToString(), Encoding.UTF8, "application/json");

            // ⚠⚠ 端点曾经写成 "https://api.minecraftservices.com/launcher/login" —— 这个端点**不存在**，
            //    一律返回 400 Bad Request，正版登录就卡在这一步。
            //    正确端点是 /authentication/login_with_xbox。
            var json = await PostJsonAsync(_httpClient, "https://api.minecraftservices.com/authentication/login_with_xbox",
                content, "Minecraft 登录");

            return (json["access_token"]?.ToString() ?? "", (long)(json["expires_in"] ?? 0));
        }

        private async Task<(string Id, string Name)> GetMinecraftProfileAsync(string mcAccessToken)
        {
            using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcAccessToken);
            var response = await _httpClient.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                // 404 = 这个账号没买 Minecraft（或没建角色）
                string hint = (int)response.StatusCode == 404
                    ? "该账号没有 Minecraft: Java 版（或还没创建角色名）"
                    : Truncate(body, 400);
                throw new Exception($"读取游戏档案失败（HTTP {(int)response.StatusCode}）\n{hint}");
            }

            var json = JObject.Parse(body);
            return (json["id"]?.ToString() ?? "", json["name"]?.ToString() ?? "");
        }

        public async Task RefreshSessionAsync(Account account)
        {
            if (account.Type != AccountType.Microsoft) return;            
            // 1. Refresh Microsoft Token
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("client_id", ClientId),
                new KeyValuePair<string, string>("refresh_token", account.RefreshToken),
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("redirect_uri", RedirectUri)
            });

            var json = await PostJsonAsync(_httpClient, "https://login.live.com/oauth20_token.srf",
                content, "刷新 Microsoft 令牌");

            string newMsAccessToken = json["access_token"]?.ToString() ?? "";
            string newRefreshToken = json["refresh_token"]?.ToString() ?? "";

            // 2. Refresh Xbox Live Token
            string xblToken = await AuthenticateXboxLiveAsync(newMsAccessToken);

            // 3. Refresh XSTS Token
            var xstsData = await AuthenticateXstsAsync(xblToken);

            // 4. Refresh Minecraft Token
            var mcTokenData = await AuthenticateMinecraftAsync(xstsData.UserHash, xstsData.Token);

            // Update Account object — AccessToken is what the launcher passes to the game,
            // so it must be refreshed too, not only MinecraftAccessToken.
            account.RefreshToken = newRefreshToken;
            account.AccessToken = mcTokenData.AccessToken;
            account.MinecraftAccessToken = mcTokenData.AccessToken;
            account.ExpiryTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + mcTokenData.ExpiresIn;
        }
        // ── 皮肤（官方账号）──────────────────────────────────────────

        /// <summary>账号正在使用的一张皮肤。</summary>
        public sealed record MinecraftSkin(string Url, string Variant, bool IsActive);

        /// <summary>账号拥有的一件披风。</summary>
        public sealed record MinecraftCape(string Url, string Alias, bool IsActive);

        /// <summary>游戏档案：皮肤 + 披风。</summary>
        public sealed record MinecraftProfile(List<MinecraftSkin> Skins, List<MinecraftCape> Capes);

        /// <summary>
        /// 拉取账号的皮肤列表。
        ///
        /// 端点是 <c>GET /minecraft/profile</c> —— 跟取角色名同一个接口，
        /// 响应里的 <c>skins[]</c> 就是皮肤，每项带 <c>url</c>
        /// （指向 textures.minecraft.net 上的 64×64 PNG）和 <c>variant</c>（CLASSIC / SLIM）。
        /// </summary>
        public async Task<List<MinecraftSkin>> GetSkinsAsync(string mcAccessToken)
            => (await GetProfileAsync(mcAccessToken)).Skins;

        /// <summary>
        /// 拉游戏档案：皮肤 + 披风。
        /// 响应里的 <c>capes[]</c> 就是披风，<c>state == "ACTIVE"</c> 那件是正在穿的。
        /// </summary>
        public async Task<MinecraftProfile> GetProfileAsync(string mcAccessToken)
        {
            using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get,
                "https://api.minecraftservices.com/minecraft/profile");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcAccessToken);

            var response = await _httpClient.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"读取皮肤失败（HTTP {(int)response.StatusCode}）\n{Truncate(body, 400)}");

            var json = JObject.Parse(body);
            var list = new List<MinecraftSkin>();

            if (json["skins"] is JArray skins)
            {
                foreach (var s in skins)
                {
                    string url = s["url"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(url)) continue;

                    // Mojang 返回的是 http://，升成 https 免得被中间件拦
                    url = url.Replace("http://", "https://");

                    list.Add(new MinecraftSkin(
                        url,
                        s["variant"]?.ToString() ?? "CLASSIC",
                        string.Equals(s["state"]?.ToString(), "ACTIVE", StringComparison.OrdinalIgnoreCase)));
                }
            }

            var capes = new List<MinecraftCape>();
            if (json["capes"] is JArray capeArr)
            {
                foreach (var c in capeArr)
                {
                    string url = c["url"]?.ToString() ?? "";
                    if (string.IsNullOrWhiteSpace(url)) continue;

                    capes.Add(new MinecraftCape(
                        url.Replace("http://", "https://"),
                        c["alias"]?.ToString() ?? "",
                        string.Equals(c["state"]?.ToString(), "ACTIVE", StringComparison.OrdinalIgnoreCase)));
                }
            }

            return new MinecraftProfile(list, capes);
        }

        /// <summary>
        /// 上传皮肤到账号（把本地 PNG 装上去）。
        ///
        /// 端点：<c>POST /minecraft/profile/skins</c>，multipart 表单，字段是
        /// <c>variant</c>（classic / slim）+ <c>file</c>（PNG 字节）。
        ///
        /// ⚠⚠ **是 POST，不是 PUT**。我一开始写的 PUT，服务端返回
        ///   <c>{"error":"METHOD_NOT_ALLOWED"}</c>。
        ///   同一个端点还有另一种用法：POST + JSON <c>{variant, url}</c> 按 URL 换皮肤。
        ///
        /// ⚠ Mojang 对这个接口有**频率限制**（大约一分钟一次），太频繁会返回 429。
        /// </summary>
        public async Task UploadSkinAsync(string mcAccessToken, byte[] pngBytes, bool slim)
        {
            // ⚠ 必须 POST（PUT 会返回 METHOD_NOT_ALLOWED）
            using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Post,
                "https://api.minecraftservices.com/minecraft/profile/skins");
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcAccessToken);

            var form = new MultipartFormDataContent();
            var file = new ByteArrayContent(pngBytes);
            file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
            form.Add(file, "file", "skin.png");
            form.Add(new StringContent(slim ? "slim" : "classic"), "variant");
            request.Content = form;

            var response = await _httpClient.SendAsync(request);
            string body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                string hint = (int)response.StatusCode switch
                {
                    429 => "换皮肤太频繁了，Mojang 限制大约一分钟一次，等一会儿再试。",
                    401 => "登录状态过期了，到「设置」里重新登录微软账号。",
                    400 when body.Contains("NOT_ALLOWED_SKIN") =>
                        "这张皮肤不被接受（可能是 64×32 旧格式，或尺寸不对）。",
                    _ => Truncate(body, 400),
                };
                throw new Exception($"上传皮肤失败（HTTP {(int)response.StatusCode}）\n{hint}");
            }
        }

        /// <summary>下载皮肤贴图（64×64 PNG）。失败返回 null。</summary>
        public async Task<byte[]?> DownloadSkinAsync(string url)
        {
            try
            {
                var bytes = await _httpClient.GetByteArrayAsync(url);
                return bytes.Length > 0 ? bytes : null;
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, $"下载皮肤 {url}");
                return null;
            }
        }

    }
}