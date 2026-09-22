using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json.Linq;
using TsuruLauncher.Models.Ecosystem;
using TsuruLauncher.Services;

namespace TsuruLauncher.Services.Ecosystem
{
    public class ModrinthService
    {
        // Simple in-memory cache for API responses
        // (locked: it is shared between the preload worker and UI-triggered searches)
        private static readonly Dictionary<string, (DateTime fetched, object? data)> _cache = new();
        private static readonly object _cacheLock = new();
        private static readonly TimeSpan _cacheDuration = TimeSpan.FromMinutes(3);

        private static HttpClient? _httpClient;
        private static string? _lastBaseUrl;
        private static readonly object _clientLock = new();

        private static HttpClient GetHttpClient()
        {
            string baseUrl;
            try
            {
                baseUrl = ConfigService.Instance?.Settings?.ModrinthApiBaseUrl;
            }
            catch
            {
                baseUrl = null;
            }
            
            if (string.IsNullOrEmpty(baseUrl))
                baseUrl = "https://api.modrinth.com/v2/";
            if (!baseUrl.EndsWith("/")) baseUrl += "/";

            lock (_clientLock)
            {
                if (_httpClient == null || _lastBaseUrl != baseUrl)
                {
                    _httpClient?.Dispose();
                    _httpClient = new HttpClient(new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                        MaxConnectionsPerServer = 10,
                        EnableMultipleHttp2Connections = true,
                        AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
                    });
                    _httpClient.BaseAddress = new Uri(baseUrl);
                    _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TsuruLauncher/2.0");
                    _httpClient.Timeout = TimeSpan.FromSeconds(30);
                    _lastBaseUrl = baseUrl;
                    lock (_cacheLock) { _cache.Clear(); }
                }
            }
            return _httpClient;
        }

        private static T? GetFromCache<T>(string key) where T : class
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(key, out var entry) && DateTime.Now - entry.fetched < _cacheDuration)
                    return entry.data as T;
                _cache.Remove(key);
                return null;
            }
        }

        private static void SetCache(string key, object? data)
        {
            lock (_cacheLock)
            {
                _cache[key] = (DateTime.Now, data);
            }
        }

        public async Task<List<ModProject>> SearchProjectsAsync(string query, int limit = 20, string sort = "relevance", string? projectType = null, int offset = 0, string? gameVersion = null)
        {
            var cacheKey = $"search_{query}_{limit}_{sort}_{projectType}_{offset}_{gameVersion}";
            var cached = GetFromCache<List<ModProject>>(cacheKey);
            if (cached != null) return cached;

            try
            {
                var url = $"search?query={Uri.EscapeDataString(query)}&limit={limit}&index={sort}&offset={offset}";
                
                // Build facets array
                var facets = new List<string>();
                if (!string.IsNullOrEmpty(projectType))
                    facets.Add($"[\"project_type:{projectType}\"]");
                if (!string.IsNullOrEmpty(gameVersion))
                    facets.Add($"[\"versions:{gameVersion}\"]");
                
                if (facets.Count > 0)
                    url += $"&facets=[{string.Join(",", facets)}]";

                var response = await GetHttpClient().GetAsync(url).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                var jsonStr = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var json = JObject.Parse(jsonStr);

                var projects = new List<ModProject>();
                var hits = json["hits"];
                if (hits != null)
                {
                    foreach (var hit in hits)
                    {
                    var rawType = hit["project_type"]?.ToString() ?? "mod";
                    projects.Add(new ModProject
                    {
                        Id = hit["project_id"]?.ToString() ?? "",
                        Name = hit["title"]?.ToString() ?? "",
                        Summary = hit["description"]?.ToString() ?? "",
                        IconUrl = hit["icon_url"]?.ToString() ?? "https://cdn.modrinth.com/assets/logo.png",
                        Author = hit["author"]?.ToString() ?? "",
                        Downloads = (long)(hit["downloads"] ?? 0),
                        Platform = ProjectPlatform.Modrinth,
                        Type = ParseProjectType(rawType),
                        WebUrl = $"https://modrinth.com/{rawType}/{hit["slug"] ?? hit["project_id"]}"
                    });
                }
                }
                SetCache(cacheKey, projects);
                return projects;
            }
            catch (Exception)
            {
                return new List<ModProject>();
            }
        }

        private static ProjectType ParseProjectType(string raw) => raw switch
        {
            "mod" => ProjectType.Mod,
            "modpack" => ProjectType.Modpack,
            "resourcepack" => ProjectType.ResourcePack,
            "shader" => ProjectType.Shader,
            "datapack" => ProjectType.DataPack,
            _ => ProjectType.Mod
        };

        /// <summary>
        /// Get trending/popular projects sorted by downloads
        /// </summary>
        public Task<List<ModProject>> GetTrendingAsync(int limit = 10, string? projectType = null, string? gameVersion = null)
            => SearchProjectsAsync("", limit, "downloads", projectType, 0, gameVersion);

        /// <summary>
        /// Get newest projects sorted by newest
        /// </summary>
        public Task<List<ModProject>> GetNewestAsync(int limit = 10, string? projectType = null, string? gameVersion = null)
            => SearchProjectsAsync("", limit, "newest", projectType, 0, gameVersion);

        /// <summary>
        /// 拉一个项目的全部版本。
        ///
        /// ⚠ 之前固定 <c>limit=50</c> 只取第一页 —— Sodium 这种项目实际有几百个版本，
        /// 结果「游戏版本」筛选下拉里只剩 5 个正式版可选（实测），分页也就 3 页封顶。
        /// 美西螈是把整份版本列表拿全了再在客户端筛/分页，这里同样按 offset 翻页翻到底
        /// （单页 100，最多 <see cref="MaxVersionPages"/> 页，防病态项目把请求打爆）。
        /// </summary>
        private const int VersionPageSize = 100;
        private const int MaxVersionPages = 8;

        public async Task<List<ModFile>> GetVersionsAsync(string projectId, string? gameVersion = null, string? loader = null)
        {
             var filters = new List<string>();

             if (!string.IsNullOrEmpty(gameVersion)) filters.Add($"game_versions=[\"{gameVersion}\"]");
             if (!string.IsNullOrEmpty(loader)) filters.Add($"loaders=[\"{loader}\"]");

             var cacheKey = $"versions_{projectId}_{gameVersion}_{loader}";
             var cached = GetFromCache<List<ModFile>>(cacheKey);
             if (cached != null) return cached;

             try
             {
                 var files = new List<ModFile>();

                 for (int page = 0; page < MaxVersionPages; page++)
                 {
                     var query = $"project/{projectId}/version?limit={VersionPageSize}&offset={page * VersionPageSize}";
                     if (filters.Any()) query += "&" + string.Join("&", filters);

                     var response = await GetHttpClient().GetAsync(query).ConfigureAwait(false);
                     if (!response.IsSuccessStatusCode) break;

                     var jsonStr = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                     var jsonArray = JArray.Parse(jsonStr);
                     if (jsonArray.Count == 0) break;

                     foreach (var v in jsonArray)
                     {
                         try
                         {
                             var primaryFile = SafeGetPrimaryFile(v);
                             if (primaryFile != null)
                             {
                                 files.Add(new ModFile
                                 {
                                     FileId = v["id"]?.ToString() ?? "",
                                     FileName = primaryFile["filename"]?.ToString() ?? "",
                                     DownloadUrl = primaryFile["url"]?.ToString() ?? "",
                                     Size = (long)(primaryFile["size"] ?? 0),
                                     ReleaseDate = v["date_published"]?.ToString() ?? "",
                                     VersionType = v["version_type"]?.ToString() ?? "release",
                                     GameVersions = SafeJsonArray<string>(v, "game_versions"),
                                     Loaders = SafeJsonArray<string>(v, "loaders"),
                                     Downloads = (long)(v["downloads"] ?? 0),
                                     Dependencies = SafeDependencies(v)
                                 });
                             }
                         }
                         catch { }
                     }

                     // 最后一页（不满一页）就收工
                     if (jsonArray.Count < VersionPageSize) break;
                 }

                 SetCache(cacheKey, files);
                 return files;
             }
             catch
             {
                 return new List<ModFile>();
             }
        }
        public async Task<ModFile?> GetVersionByHashAsync(string hash, string algorithm = "sha1")
        {
            try
            {
                var response = await GetHttpClient().GetAsync($"version_file/{hash}?algorithm={algorithm}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;

                var jsonStr = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var v = JObject.Parse(jsonStr);
                
                var primaryFile = v["files"]?.FirstOrDefault(f => (bool?)f["primary"] == true) ?? v["files"]?.FirstOrDefault();
                if (primaryFile == null) return null;

                return new ModFile
                {
                    FileId = v["id"]?.ToString() ?? "",
                    ProjectId = v["project_id"]?.ToString() ?? "",
                    FileName = primaryFile["filename"]?.ToString() ?? "",
                    DownloadUrl = primaryFile["url"]?.ToString() ?? "",
                    Size = (long)(primaryFile["size"] ?? 0),
                    // We can add hashes here if needed for index
                    Hashes = new Dictionary<string, string>
                    {
                        { "sha1", primaryFile["hashes"]?["sha1"]?.ToString() ?? "" },
                        { "sha512", primaryFile["hashes"]?["sha512"]?.ToString() ?? "" }
                    }
                };
            }
            catch
            {
                return null;
            }
        }

        public async Task<ResourceDetail?> GetProjectDetailAsync(string projectId)
        {
            var cacheKey = $"detail_{projectId}";
            var cached = GetFromCache<ResourceDetail>(cacheKey);
            if (cached != null) return cached;

            try
            {
                var response = await GetHttpClient().GetAsync($"project/{projectId}").ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) return null;
                
                var jsonStr = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var json = JObject.Parse(jsonStr);

                var rawType = json["project_type"]?.ToString() ?? "mod";
                var publishedStr = json["published"]?.ToString();
                var updatedStr = json["updated"]?.ToString();

                var detail = new ResourceDetail
                {
                    Id = json["id"]?.ToString() ?? "",
                    Name = json["title"]?.ToString() ?? "",
                    Summary = json["description"]?.ToString() ?? "",
                    Description = StripMarkdown(json["body"]?.ToString() ?? ""),
                    IconUrl = json["icon_url"]?.ToString() ?? "https://cdn.modrinth.com/assets/logo.png",
                    Author = SafeNavigateString(json, new[] { "team", "members", "[0]", "user", "username" }),
                    Downloads = (long)(json["downloads"] ?? 0),
                    Followers = (int)(json["followers"] ?? 0),
                    DateCreated = ParseModrinthDate(publishedStr),
                    DateModified = ParseModrinthDate(updatedStr),
                    License = NonEmpty(SafeNavigateString(json, new[] { "license", "name" })) 
                           ?? NonEmpty(SafeNavigateString(json, new[] { "license", "id" })) 
                           ?? "Unknown",
                    Platform = ProjectPlatform.Modrinth,
                    Type = ParseProjectType(rawType),
                    WebUrl = $"https://modrinth.com/{rawType}/{json["slug"] ?? projectId}",
                    Categories = SafeJsonArray<string>(json, "categories"),
                    GameVersions = SafeJsonArray<string>(json, "game_versions"),
                    // ⚠ 之前这里错把 client_side/server_side 当成 loaders —— 那是"支持的环境"，
                    //   真正的加载器列表在 json["loaders"]（fabric / forge / neoforge / quilt …）。
                    Loaders = SafeJsonArray<string>(json, "loaders"),
                    GalleryImages = SafeGalleryImages(json),
                    // 相关链接（Axolotl 右栏「相关链接」区）
                    SourceUrl = json["source_url"]?.ToString() ?? string.Empty,
                    IssuesUrl = json["issues_url"]?.ToString() ?? string.Empty,
                    WikiUrl = json["wiki_url"]?.ToString() ?? string.Empty,
                    DiscordUrl = json["discord_url"]?.ToString() ?? string.Empty,
                    DonationUrl = json["donation_urls"] is JArray don && don.Count > 0
                                  ? don[0]?["url"]?.ToString() ?? string.Empty : string.Empty
                };

                detail.IsClientSideOnly = json["client_side"]?.ToString() == "required";
                detail.IsServerSideOnly = json["server_side"]?.ToString() == "required";

                // 作者：GET /team/{teamId}/members（project 详情里只有 team id）
                // ⚠ 没有 /team/{id} 这个路由（实测 404），团队名在公开 v2 API 里拿不到 ——
                //   Axolotl 显示的 "CaffeineMC" 来自它自己的后端。这里退而求其次取
                //   「Project Lead」（Sodium → jellysquid3），至少不会是空串。
                try
                {
                    string teamId = json["team"]?.ToString() ?? string.Empty;
                    if (!string.IsNullOrEmpty(teamId))
                    {
                        var tr = await GetHttpClient().GetAsync($"team/{teamId}/members").ConfigureAwait(false);
                        if (tr.IsSuccessStatusCode)
                        {
                            var arr = JArray.Parse(await tr.Content.ReadAsStringAsync().ConfigureAwait(false));
                            var list = new List<ResourceAuthor>();
                            foreach (var m in arr)
                            {
                                list.Add(new ResourceAuthor
                                {
                                    Name = m["user"]?["username"]?.ToString() ?? string.Empty,
                                    AvatarUrl = m["user"]?["avatar_url"]?.ToString() ?? string.Empty,
                                    Role = m["role"]?.ToString() ?? string.Empty,
                                    IsOrganization = string.Equals(m["role"]?.ToString(), "Organization", StringComparison.OrdinalIgnoreCase),
                                    Url = "https://modrinth.com/user/" + (m["user"]?["username"]?.ToString() ?? string.Empty)
                                });
                            }
                            detail.Authors = list;

                            if (string.IsNullOrWhiteSpace(detail.Author))
                            {
                                var lead = list.FirstOrDefault(a => a.Role.Contains("Lead", StringComparison.OrdinalIgnoreCase))
                                           ?? list.FirstOrDefault(a => a.Role.Contains("Owner", StringComparison.OrdinalIgnoreCase))
                                           ?? list.FirstOrDefault();
                                if (lead != null) detail.Author = lead.Name;
                            }
                        }
                    }
                }
                catch { }

                SetCache(cacheKey, detail);
                return detail;
            }
            catch
            {
                return null;
            }
        }

        private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        private static string? SafeNavigateString(JToken root, string[] path)
        {
            try
            {
                JToken current = root;
                foreach (var key in path)
                {
                    if (current == null) return null;
                    if (key.StartsWith("[") && key.EndsWith("]"))
                    {
                        int idx = int.Parse(key.Trim('[', ']'));
                        if (current is JArray arr && idx < arr.Count)
                            current = arr[idx];
                        else
                            return null;
                    }
                    else
                    {
                        current = current[key];
                    }
                }
                return current?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static List<T> SafeJsonArray<T>(JToken root, string key) where T : class
        {
            try
            {
                var token = root[key];
                if (token == null) return new List<T>();
                return token.ToObject<List<T>>() ?? new List<T>();
            }
            catch
            {
                return new List<T>();
            }
        }

        private static List<GalleryImage> SafeGalleryImages(JToken json)
        {
            try
            {
                var gallery = json["gallery"];
                if (gallery == null || !gallery.Any()) return new List<GalleryImage>();

                var urls = new List<GalleryImage>();
                foreach (var item in gallery)
                {
                    try
                    {
                        var url = item["url"]?.ToString();
                        if (!string.IsNullOrEmpty(url))
                            urls.Add(new GalleryImage { Url = url! });
                    }
                    catch { }
                }
                return urls;
            }
            catch
            {
                return new List<GalleryImage>();
            }
        }

        private static JToken? SafeGetPrimaryFile(JToken version)
        {
            try
            {
                var files = version["files"];
                if (files == null || !files.Any()) return null;

                foreach (var f in files)
                {
                    if ((bool?)f["primary"] == true) return f;
                }
                return files.FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static List<ModDependency> SafeDependencies(JToken version)
        {
            try
            {
                var deps = version["dependencies"];
                if (deps == null || !deps.Any()) return new List<ModDependency>();

                var result = new List<ModDependency>();
                foreach (var d in deps)
                {
                    result.Add(new ModDependency
                    {
                        ProjectId = d["project_id"]?.ToString() ?? string.Empty,
                        VersionId = d["version_id"]?.ToString() ?? string.Empty,
                        FileName = d["file_name"]?.ToString() ?? string.Empty,   // ⚠ Modrinth 的键是 file_name，不是 filename
                        DependencyType = d["dependency_type"]?.ToString() ?? "required"
                    });
                }
                return result;
            }
            catch
            {
                return new List<ModDependency>();
            }
        }

        private DateTime ParseModrinthDate(string? dateStr)
        {
            if (string.IsNullOrEmpty(dateStr)) return DateTime.Now;

            string[] formats = {
                "yyyy-MM-ddTHH:mm:ss.fffZ",
                "yyyy-MM-ddTHH:mm:ssZ",
                "yyyy-MM-ddTHH:mm:ss.fffffffZ",
                "yyyy-MM-dd'T'HH:mm:ss.fffK",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd"
            };

            foreach (var fmt in formats)
            {
                if (DateTime.TryParseExact(dateStr, fmt, null, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var result))
                    return result;
            }

            if (DateTime.TryParse(dateStr, out var fallback)) return fallback;

            System.Diagnostics.Debug.WriteLine($"[Modrinth] Failed to parse date: '{dateStr}'");
            return DateTime.Now;
        }

        private string StripMarkdown(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return "";

            var text = System.Text.RegularExpressions.Regex.Replace(markdown, @"!\[.*?\]\(.*?\)", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"#{1,6}\s*", "");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.+?)\*\*", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\*(.+?)\*", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"`(.+?)`", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"~~(.+?)~~", "$1");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^>\s?", "", System.Text.RegularExpressions.RegexOptions.Multiline);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"^[-*+]\s+", "- ", System.Text.RegularExpressions.RegexOptions.Multiline);
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\n{3,}", "\n\n");

            return text.Trim();
        }

        public async Task<List<ModFile>> GetFilteredVersionsAsync(string projectId, string? gameVersion = null, string? loader = null)
        {
            var allVersions = await GetVersionsAsync(projectId);

            if (!string.IsNullOrEmpty(gameVersion))
            {
                allVersions = allVersions.Where(v => v.GameVersions.Contains(gameVersion) || v.GameVersions.Count == 0).ToList();
            }

            if (!string.IsNullOrEmpty(loader))
            {
                allVersions = allVersions.Where(v => v.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase) || v.Loaders.Count == 0).ToList();
            }

            return allVersions.OrderByDescending(v => v.ReleaseDate).ToList();
        }
    }
}