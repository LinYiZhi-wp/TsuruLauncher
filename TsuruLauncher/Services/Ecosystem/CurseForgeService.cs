using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TsuruLauncher.Models.Ecosystem;
using TsuruLauncher.Utilities;

namespace TsuruLauncher.Services.Ecosystem
{
    public class CurseForgeException : Exception
    {
        public CurseForgeException(string message) : base(message) { }
    }

    /// <summary>
    /// CurseForge 内容源（第二个资源站）。
    /// 官方 API 需要用户自备 API Key：https://console.curseforge.com/ → API Keys。
    /// 未配置 Key 时所有调用都会抛出可读的异常，由界面向导用户去设置里填写。
    /// </summary>
    public class CurseForgeService
    {
        public const string BaseUrl = "https://api.curseforge.com/v1";
        private const int MinecraftGameId = 432;

        private readonly HttpClient _http;

        public CurseForgeService()
        {
            _http = HttpClientFactory.Client;
        }

        public static string ApiKey
        {
            get
            {
                try { return (ConfigService.Instance.Settings.CurseForgeApiKey ?? string.Empty).Trim(); }
                catch { return string.Empty; }
            }
        }

        public static bool IsConfigured => ApiKey.Length > 20;

        // ── 映射表 ────────────────────────────────────────────
        public static int ClassIdFor(string? projectType)
        {
            switch ((projectType ?? "mod").ToLowerInvariant())
            {
                case "modpack": return 4471;
                case "resourcepack": return 12;
                case "shader": return 6552;
                case "datapack": return 6945;
                default: return 6; // mods
            }
        }

        public static ProjectType ProjectTypeFor(int classId)
        {
            switch (classId)
            {
                case 4471: return ProjectType.Modpack;
                case 12: return ProjectType.ResourcePack;
                case 6552: return ProjectType.Shader;
                case 6945: return ProjectType.DataPack;
                default: return ProjectType.Mod;
            }
        }

        public static int LoaderIdFor(string? loader)
        {
            switch ((loader ?? string.Empty).ToLowerInvariant())
            {
                case "forge": return 1;
                case "fabric": return 4;
                case "quilt": return 5;
                case "neoforge": return 6;
                default: return 0;
            }
        }

        public static string LoaderNameFor(int modLoader)
        {
            switch (modLoader)
            {
                case 1: return "Forge";
                case 4: return "Fabric";
                case 5: return "Quilt";
                case 6: return "NeoForge";
                default: return string.Empty;
            }
        }

        // ── 搜索 / 热门 / 最新 ────────────────────────────────
        public async Task<List<ModProject>> SearchProjectsAsync(string query, int limit = 20, string sort = "relevance",
            string? projectType = null, int offset = 0, string? gameVersion = null)
        {
            int sortField = sort == "downloads" ? 6 : sort == "updated" ? 3 : 2; // 2=Popularity, 3=LastUpdated, 6=TotalDownloads
            var url = new StringBuilder(BaseUrl + "/mods/search?gameId=" + MinecraftGameId);
            url.Append("&classId=").Append(ClassIdFor(projectType));
            url.Append("&index=").Append(Math.Max(0, offset));
            url.Append("&pageSize=").Append(Math.Clamp(limit, 1, 50));
            url.Append("&sortField=").Append(sortField).Append("&sortOrder=desc");
            if (!string.IsNullOrWhiteSpace(query)) url.Append("&searchFilter=").Append(Uri.EscapeDataString(query.Trim()));
            if (!string.IsNullOrWhiteSpace(gameVersion)) url.Append("&gameVersion=").Append(Uri.EscapeDataString(gameVersion.Trim()));

            var json = await GetAsync(url.ToString());
            return ParseProjects((JArray?)json["data"] ?? new JArray());
        }

        public Task<List<ModProject>> GetTrendingAsync(int limit = 10, string? projectType = null, string? gameVersion = null)
            => SearchProjectsAsync(string.Empty, limit, "downloads", projectType, 0, gameVersion);

        public Task<List<ModProject>> GetNewestAsync(int limit = 10, string? projectType = null, string? gameVersion = null)
            => SearchProjectsAsync(string.Empty, limit, "updated", projectType, 0, gameVersion);

        // ── 文件版本 ──────────────────────────────────────────
        public async Task<List<ModFile>> GetVersionsAsync(string projectId, string? gameVersion = null, string? loader = null)
        {
            if (!long.TryParse(projectId, out _)) throw new CurseForgeException("CurseForge 的项目 ID 必须是数字。");

            var url = new StringBuilder(BaseUrl + "/mods/" + projectId + "/files?pageSize=50");
            if (!string.IsNullOrWhiteSpace(gameVersion)) url.Append("&gameVersion=").Append(Uri.EscapeDataString(gameVersion.Trim()));
            int loaderId = LoaderIdFor(loader);
            if (loaderId > 0) url.Append("&modLoaderType=").Append(loaderId);

            var json = await GetAsync(url.ToString());
            var files = new List<ModFile>();
            foreach (var item in (JArray?)json["data"] ?? new JArray())
            {
                var file = ParseFile(item, projectId);
                if (file != null) files.Add(file);
            }
            return files;
        }

        public async Task<ModFile?> GetFileAsync(string projectId, string fileId)
        {
            var json = await GetAsync(BaseUrl + "/mods/" + projectId + "/files/" + fileId);
            return ParseFile(json["data"], projectId);
        }

        private static ModFile? ParseFile(JToken? item, string projectId)
        {
            if (item == null) return null;
            long id = (long?)item["id"] ?? 0;
            string fileName = (string?)item["fileName"] ?? ("file-" + id + ".jar");

            string downloadUrl = (string?)item["downloadUrl"] ?? string.Empty;
            // 作者禁止 API 分发时 downloadUrl 为 null —— 用 CurseForge CDN 的固定规则推导直链
            if (string.IsNullOrEmpty(downloadUrl) && id > 0)
                downloadUrl = "https://mediafilez.forgecdn.net/files/" + (id / 1000) + "/" + (id % 1000).ToString("000") + "/" + fileName;

            var file = new ModFile
            {
                FileId = id.ToString(CultureInfo.InvariantCulture),
                FileName = fileName,
                DownloadUrl = downloadUrl,
                ProjectId = projectId,
                Size = (long?)item["fileLength"] ?? 0,
                ReleaseDate = ParseDate((string?)item["fileDate"]),
                // CurseForge: releaseType 1=release 2=beta 3=alpha → 归一化成 Modrinth 的字符串
                VersionType = ((int?)item["releaseType"] ?? 1) switch { 2 => "beta", 3 => "alpha", _ => "release" },
                GameVersions = new List<string>(),
                Loaders = new List<string>()
            };

            foreach (var gv in (JArray?)item["gameVersions"] ?? new JArray())
            {
                string value = gv.ToString();
                string loaderName = LoaderNameFor(LoaderIdFor(value));
                if (loaderName.Length > 0) file.Loaders.Add(loaderName);
                else file.GameVersions.Add(value);
            }

            foreach (var hash in (JArray?)item["hashes"] ?? new JArray())
            {
                string algo = ((int?)hash["algo"] ?? 0) == 1 ? "sha1" : "md5";
                string value = ((string?)hash["value"] ?? string.Empty).ToLowerInvariant();
                if (value.Length > 0) file.Hashes[algo] = value;
            }

            foreach (var dep in (JArray?)item["dependencies"] ?? new JArray())
            {
                int relation = (int?)dep["relationType"] ?? 3;
                file.Dependencies.Add(new ModDependency
                {
                    ProjectId = ((long?)dep["modId"] ?? 0).ToString(CultureInfo.InvariantCulture),
                    DependencyType = relation == 3 ? "required" : relation == 2 ? "optional" : "embedded"
                });
            }

            if (file.GameVersions.Count == 0 && file.Loaders.Count == 0)
            {
                string? fallback = (string?)item["gameVersions"]?[0];
                if (!string.IsNullOrEmpty(fallback)) file.GameVersions.Add(fallback);
            }

            return file;
        }

        // ── 详情 ──────────────────────────────────────────────
        public async Task<ResourceDetail?> GetProjectDetailAsync(string projectId)
        {
            var json = await GetAsync(BaseUrl + "/mods/" + projectId);
            var data = json["data"];
            if (data == null) return null;

            string description = string.Empty;
            try
            {
                var desc = await GetAsync(BaseUrl + "/mods/" + projectId + "/description");
                description = HtmlToText((string?)desc["data"] ?? string.Empty);
            }
            catch { }

            var project = ParseProject(data);
            return new ResourceDetail
            {
                Id = project.Id,
                Name = project.Name,
                Summary = project.Summary,
                Description = description,
                IconUrl = project.IconUrl,
                Author = project.Author,
                Downloads = project.Downloads,
                Platform = ProjectPlatform.CurseForge,
                Type = project.Type,
                WebUrl = project.WebUrl,
                Categories = new List<string>(),
                GameVersions = new List<string>(),
                Loaders = new List<string>(),
                    Authors = SafeCurseAuthors(data),
                    SourceUrl = data["links"]?["sourceUrl"]?.ToString() ?? string.Empty,
                    IssuesUrl = data["links"]?["issuesUrl"]?.ToString() ?? string.Empty,
                    WikiUrl = data["links"]?["wikiUrl"]?.ToString() ?? string.Empty,
                    DiscordUrl = data["links"]?["discordUrl"]?.ToString() ?? string.Empty
            };
        }

        private static List<ModProject> ParseProjects(JArray data)
        {
            var list = new List<ModProject>();
            foreach (var item in data)
            {
                var project = ParseProject(item);
                if (project != null) list.Add(project);
            }
            return list;
        }

        private static ModProject ParseProject(JToken item)
        {
            long id = (long?)item["id"] ?? 0;
            string slug = (string?)item["slug"] ?? id.ToString(CultureInfo.InvariantCulture);
            int classId = (int?)item["classId"] ?? 6;
            ProjectType type = ProjectTypeFor(classId);

            string author = (string?)item["authors"]?[0]?["name"] ?? string.Empty;

            return new ModProject
            {
                Id = id.ToString(CultureInfo.InvariantCulture),
                Name = (string?)item["name"] ?? slug,
                Summary = (string?)item["summary"] ?? string.Empty,
                IconUrl = (string?)item["logo"]?["thumbnailUrl"] ?? (string?)item["logo"]?["url"] ?? string.Empty,
                Author = author,
                Downloads = (long?)item["downloadCount"] ?? 0,
                Platform = ProjectPlatform.CurseForge,
                Type = type,
                WebUrl = (string?)item["links"]?["websiteUrl"] ?? ("https://www.curseforge.com/minecraft/" + WebSegment(type) + "/" + slug)
            };
        }

        private static string WebSegment(ProjectType type)
        {
            switch (type)
            {
                case ProjectType.Modpack: return "modpacks";
                case ProjectType.ResourcePack: return "texture-packs";
                case ProjectType.Shader: return "shaders";
                case ProjectType.DataPack: return "data-packs";
                default: return "mc-mods";
            }
        }

        // ── HTTP ─────────────────────────────────────────────
        private async Task<JObject> GetAsync(string url)
        {
            string key = ApiKey;
            if (key.Length == 0)
                throw new CurseForgeException("还没有配置 CurseForge API Key。请在「设置 → 下载」里填入自己的 Key（可在 console.curseforge.com 免费申请）。");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("x-api-key", key);
            request.Headers.Add("Accept", "application/json");

            using var resp = await _http.SendAsync(request);
            string body = await resp.Content.ReadAsStringAsync();

            if (!resp.IsSuccessStatusCode)
            {
                if ((int)resp.StatusCode == 403) throw new CurseForgeException("CurseForge 拒绝了请求（403）：API Key 无效或已过期。");
                if ((int)resp.StatusCode == 429) throw new CurseForgeException("CurseForge 请求过于频繁（429），请稍后再试。");
                throw new CurseForgeException("CurseForge 接口错误 (" + (int)resp.StatusCode + ")");
            }

            try { return JObject.Parse(body); }
            catch (Exception ex) { throw new CurseForgeException("无法解析 CurseForge 返回的数据: " + ex.Message); }
        }

        private static string ParseDate(string? value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var dt)
                ? dt.ToString("yyyy-MM-dd")
                : value;
        }

        /// <summary>CurseForge 的简介是 HTML，粗略转成纯文本。</summary>
        public static string HtmlToText(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            string text = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", string.Empty, RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "</p>", "\n\n", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<li>", "· ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", string.Empty);
            text = System.Net.WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, "\n{3,}", "\n\n");
            return text.Trim();
        }
        /// <summary>CurseForge 的作者是 <c>authors:[{name,url}]</c>（无头像，角色统一标"作者"）。</summary>
        private static List<ResourceAuthor> SafeCurseAuthors(JToken data)
        {
            var list = new List<ResourceAuthor>();
            try
            {
                if (data["authors"] is JArray arr)
                    foreach (var a in arr)
                        list.Add(new ResourceAuthor
                        {
                            Name = a["name"]?.ToString() ?? string.Empty,
                            Url = a["url"]?.ToString() ?? string.Empty,
                            Role = "作者"
                        });
            }
            catch { }
            return list;
        }

    }
}
