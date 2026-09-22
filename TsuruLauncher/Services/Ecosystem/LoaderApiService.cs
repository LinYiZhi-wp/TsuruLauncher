using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using TsuruLauncher.ViewModels;

namespace TsuruLauncher.Services.Ecosystem
{
    public class LoaderApiService
    {
        private const string BMCLAPI = "https://bmclapi2.bangbang93.com";

        public List<LoaderVersionItem> GetForgeVersions(string mcVersion)
        {
            var results = new List<LoaderVersionItem>();
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                string url = $"{BMCLAPI}/forge/minecraft/{Uri.EscapeDataString(mcVersion)}";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                string json = client.GetStringAsync(url, cts.Token).GetAwaiter().GetResult();
                var versions = Newtonsoft.Json.Linq.JArray.Parse(json);

                foreach (var v in versions.OrderByDescending(v =>
                {
                    int.TryParse(v["build"]?.ToString(), out int b);
                    return b;
                }).Take(15))
                {
                    results.Add(new LoaderVersionItem { Version = v["version"]?.ToString() ?? "", Type = "正式版" });
                }
            }
            catch { }

            return results;
        }

        public List<LoaderVersionItem> GetFabricVersions(string mcVersion)
        {
            var results = new List<LoaderVersionItem>();
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                string url = $"{BMCLAPI}/fabric-meta/v2/versions/loader/{Uri.EscapeDataString(mcVersion)}";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                string json = client.GetStringAsync(url, cts.Token).GetAwaiter().GetResult();
                var versions = Newtonsoft.Json.Linq.JArray.Parse(json);

                foreach (var v in versions)
                {
                    var loader = v["loader"];
                    if (loader == null) continue;
                    string ver = loader["version"]?.ToString() ?? "";
                    bool isStable = loader["stable"]?.ToObject<bool>() == true;
                    results.Add(new LoaderVersionItem { Version = ver, Type = isStable ? "稳定版" : "测试版" });
                }
            }
            catch { }

            return results;
        }

        public List<LoaderVersionItem> GetOptiFineVersions(string mcVersion)
        {
            var results = new List<LoaderVersionItem>();
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
                string url = $"{BMCLAPI}/optifine/versionList";
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                string json = client.GetStringAsync(url, cts.Token).GetAwaiter().GetResult();
                var versions = Newtonsoft.Json.Linq.JArray.Parse(json);

                foreach (var v in versions.Where(v => v["mcversion"]?.ToString() == mcVersion)
                    .OrderByDescending(v => v["type"]?.ToString())
                    .ThenByDescending(v => v["patch"]?.ToString()))
                {
                    string typeStr = v["type"]?.ToString() ?? "";
                    string patch = v["patch"]?.ToString() ?? "";
                    string displayVer = string.IsNullOrEmpty(patch) ? typeStr : $"{typeStr}_{patch}";
                    results.Add(new LoaderVersionItem { Version = displayVer, Type = typeStr });
                }
            }
            catch { }

            return results;
        }
    }
}