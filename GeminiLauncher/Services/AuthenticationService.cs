using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using GeminiLauncher.Models;
using System.Diagnostics;
using System.Windows;

namespace GeminiLauncher.Services
{
    public class AuthenticationService
    {
        private const string ClientId = "00000000402b5328"; // Azure Client ID (commonly used for Minecraft Launchers)
        private const string RedirectUri = "https://login.live.com/oauth20_desktop.srf";
        private const string Scope = "XboxLive.Signin offline_access";
        
        private readonly HttpClient _httpClient;

        public AuthenticationService()
        {
            _httpClient = new HttpClient();
        }

        // --- Offline Login ---
        public Account LoginOffline(string username)
        {
            return new Account
            {
                Username = username,
                Uuid = GenerateOfflineUuid(username),
                AccessToken = Guid.NewGuid().ToString("N"),
                Type = AccountType.Offline,
                AvatarUrl = $"https://minotar.net/helm/{username}/100.png"
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
                Type = AccountType.Microsoft,
                AvatarUrl = $"https://minotar.net/helm/{profile.Name}/100.png"
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
                new KeyValuePair<string, string>("redirect_uri", RedirectUri)
            });

            var response = await _httpClient.PostAsync("https://login.live.com/oauth20_token.srf", content);
            response.EnsureSuccessStatusCode();
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            
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
            var response = await _httpClient.PostAsync("https://user.auth.xboxlive.com/user/authenticate", content);
            response.EnsureSuccessStatusCode();
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            
            return json["Token"]?.ToString() ?? throw new Exception("Xbox Live Auth Failed: No Token");
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
            response.EnsureSuccessStatusCode();
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            
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
            var response = await _httpClient.PostAsync("https://api.minecraftservices.com/launcher/login", content);
            response.EnsureSuccessStatusCode();
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            
            return (json["access_token"]?.ToString() ?? "", (long)(json["expires_in"] ?? 0));
        }

        private async Task<(string Id, string Name)> GetMinecraftProfileAsync(string mcAccessToken)
        {
            using var request = new HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", mcAccessToken);
            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());
            
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

            var response = await _httpClient.PostAsync("https://login.live.com/oauth20_token.srf", content);
            response.EnsureSuccessStatusCode();
            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

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
    }
}
