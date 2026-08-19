using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using GeminiLauncher.Models;
using Newtonsoft.Json.Linq;

namespace GeminiLauncher.Services.Network
{
    public class VersionManifestService
    {
        private static readonly Dictionary<string, string> Sources = new()
        {
            { "BMCLAPI", "https://bmclapi2.bangbang93.com/mc/game/version_manifest.json" },
            { "Official", "https://piston-meta.mojang.com/mc/game/version_manifest.json" },
            { "FastMirror", "https://download.fastmirror.net/mc/game/version_manifest.json" },
            { "MCMirror", "https://mirrors.mcfx.net/mc/game/version_manifest.json" }
        };

        public static List<string> AvailableSources => Sources.Keys.ToList();

        // Cache is keyed by source: switching the download source must not serve
        // a manifest fetched from another mirror.
        private static readonly Dictionary<string, (List<DownloadableVersion> versions, DateTime fetched)> _cache = new();
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);
        private static readonly object _cacheLock = new();

        public async Task<List<DownloadableVersion>> GetVersionsAsync(string source = "BMCLAPI")
        {
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(source, out var entry) && (DateTime.Now - entry.fetched) < CacheDuration)
                {
                    return entry.versions;
                }
            }

            var versions = await TryFetchAsync(source);
            if (versions != null) return versions;

            foreach (var s in Sources.Keys.Where(k => k != source))
            {
                versions = await TryFetchAsync(s);
                if (versions != null) return versions;
            }

            lock (_cacheLock)
            {
                if (_cache.TryGetValue(source, out var entry)) return entry.versions;
                return new List<DownloadableVersion>();
            }
        }

        private async Task<List<DownloadableVersion>?> TryFetchAsync(string sourceName)
        {
            if (!Sources.TryGetValue(sourceName, out var url)) return null;

            try
            {
                var json = await HttpClientFactory.Client.GetStringAsync(url);
                var jObject = JObject.Parse(json);
                var versions = jObject["versions"]?.ToObject<List<DownloadableVersion>>();

                if (versions != null && versions.Any())
                {
                    lock (_cacheLock)
                    {
                        _cache[sourceName] = (versions, DateTime.Now);
                    }
                    return versions;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to fetch from {sourceName}: {ex.Message}");
            }
            return null;
        }
    }
}
