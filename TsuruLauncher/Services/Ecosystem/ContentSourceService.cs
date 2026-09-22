using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TsuruLauncher.Models.Ecosystem;

namespace TsuruLauncher.Services.Ecosystem
{
    /// <summary>
    /// 内容源路由：在 Modrinth 与 CurseForge 之间切换，视图模型只跟它打交道。
    /// </summary>
    public class ContentSourceService
    {
        private readonly ModrinthService _modrinth = new ModrinthService();
        private readonly CurseForgeService _curseForge = new CurseForgeService();

        public ProjectPlatform Platform { get; set; } = ProjectPlatform.Modrinth;

        public bool IsCurseForge => Platform == ProjectPlatform.CurseForge;

        public static string DisplayName(ProjectPlatform platform)
            => platform == ProjectPlatform.CurseForge ? "CurseForge" : "Modrinth";

        public async Task<List<ModProject>> SearchProjectsAsync(string query, int limit = 20, string sort = "relevance",
            string? projectType = null, int offset = 0, string? gameVersion = null)
        {
            if (!IsCurseForge)
                return await _modrinth.SearchProjectsAsync(query, limit, sort, projectType, offset, gameVersion);
            return await _curseForge.SearchProjectsAsync(query, limit, sort, projectType, offset, gameVersion);
        }

        public async Task<List<ModProject>> GetTrendingAsync(int limit = 10, string? projectType = null, string? gameVersion = null)
        {
            if (!IsCurseForge) return await _modrinth.GetTrendingAsync(limit, projectType, gameVersion);
            return await _curseForge.GetTrendingAsync(limit, projectType, gameVersion);
        }

        public async Task<List<ModProject>> GetNewestAsync(int limit = 10, string? projectType = null, string? gameVersion = null)
        {
            if (!IsCurseForge) return await _modrinth.GetNewestAsync(limit, projectType, gameVersion);
            return await _curseForge.GetNewestAsync(limit, projectType, gameVersion);
        }

        /// <summary>platform 传 null 时用当前选中的源。</summary>
        public async Task<List<ModFile>> GetVersionsAsync(string projectId, string? gameVersion = null, string? loader = null,
            ProjectPlatform? platform = null)
        {
            ProjectPlatform target = platform ?? Platform;
            if (target == ProjectPlatform.CurseForge)
                return await _curseForge.GetVersionsAsync(projectId, gameVersion, loader);
            return await _modrinth.GetVersionsAsync(projectId, gameVersion, loader);
        }

        public async Task<ResourceDetail?> GetProjectDetailAsync(string projectId, ProjectPlatform? platform = null)
        {
            ProjectPlatform target = platform ?? Platform;
            if (target == ProjectPlatform.CurseForge)
                return await _curseForge.GetProjectDetailAsync(projectId);
            return await _modrinth.GetProjectDetailAsync(projectId);
        }

        /// <summary>把 CurseForge 的版本号（字符串）转成可比较的日期字符串。</summary>
        public static string DescribeSource(ProjectPlatform platform, int resultCount)
            => DisplayName(platform) + " · " + resultCount + " 个结果";
    }
}
