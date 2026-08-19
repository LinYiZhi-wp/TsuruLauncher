using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using GeminiLauncher.Services.Network;

namespace GeminiLauncher.Services
{
    public class InstallationService
    {
        private readonly DownloadService _downloadService;
        private readonly string _gamePath;
        private readonly string _downloadSource; // e.g. "https://piston-meta.mojang.com"

        public InstallationService(string gamePath, string downloadSource = "Official")
        {
            _gamePath = gamePath;
            int threads = Math.Max(1, ConfigService.Instance.Settings.MaxDownloadThreads);
            _downloadService = new DownloadService(maxConcurrency: threads);
            _downloadSource = downloadSource == "Official" 
                ? "https://piston-meta.mojang.com" 
                : "https://bmclapi2.bangbang93.com";
        }

        public async Task InstallVersionAsync(string versionId, IProgress<double> progress)
        {
            // 1. Get Version Manifest
            string manifestUrl = _downloadSource == "Official" 
                ? "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json"
                : $"{_downloadSource}/mc/game/version_manifest_v2.json";

            using var client = new System.Net.Http.HttpClient();
            var manifestStr = await client.GetStringAsync(manifestUrl);
            var manifest = JObject.Parse(manifestStr);

            // 2. Find Version JSON URL
            var versionEntry = manifest["versions"]?.FirstOrDefault(v => v["id"]?.ToString() == versionId);
            if (versionEntry == null) throw new Exception($"Version {versionId} not found in manifest.");

            string jsonUrl = versionEntry["url"]?.ToString() ?? "";
            
            // Mirror fix for version json url if needed
            if (_downloadSource != "Official")
            {
                jsonUrl = jsonUrl.Replace("https://piston-meta.mojang.com", _downloadSource);
                jsonUrl = jsonUrl.Replace("https://launchermeta.mojang.com", _downloadSource);
            }

            // 3. Download Version JSON
            string jsonPath = Path.Combine(_gamePath, "versions", versionId, $"{versionId}.json");
            await _downloadService.DownloadFileAsync(jsonUrl, jsonPath);

            var versionJson = JObject.Parse(File.ReadAllText(jsonPath));
            var downloads = new List<DownloadRequest>();

            // 4. Client Jar
            var clientDownload = versionJson["downloads"]?["client"];
            if (clientDownload != null)
            {
                string clientUrl = clientDownload["url"]?.ToString() ?? "";
                string clientSha1 = clientDownload["sha1"]?.ToString() ?? "";
                string clientPath = Path.Combine(_gamePath, "versions", versionId, $"{versionId}.jar");
                
                if (_downloadSource != "Official")
                {
                    clientUrl = clientUrl.Replace("https://piston-data.mojang.com", _downloadSource);
                    clientUrl = clientUrl.Replace("https://launcher.mojang.com", _downloadSource);
                }

                downloads.Add(new DownloadRequest { Url = clientUrl, DestinationPath = clientPath, Sha1 = clientSha1 });
            }

            // 5. Libraries
            var libraries = versionJson["libraries"] as JArray;
            if (libraries != null)
            {
                foreach (var lib in libraries)
                {
                    // Check rules (simplified: allow if no rules or if rules allow os)
                    if (!IsLibraryAllowed(lib)) continue;

                    var artifact = lib["downloads"]?["artifact"];
                    if (artifact != null)
                    {
                        string path = artifact["path"]?.ToString() ?? "";
                        string url = artifact["url"]?.ToString() ?? "";
                        string sha1 = artifact["sha1"]?.ToString() ?? "";
                        
                        if (!string.IsNullOrEmpty(path))
                        {
                            if (_downloadSource != "Official")
                            {
                                url = url.Replace("https://libraries.minecraft.net", $"{_downloadSource}/maven");
                            }
                            
                            downloads.Add(new DownloadRequest 
                            { 
                                Url = url, 
                                DestinationPath = Path.Combine(_gamePath, "libraries", path), 
                                Sha1 = sha1 
                            });
                        }
                    }
                    
                    // Natives (classifiers) — required for LWJGL at launch
                    var natives = lib["natives"];
                    if (natives != null)
                    {
                        string? classifier = natives["windows"]?.ToString();
                        if (!string.IsNullOrEmpty(classifier))
                            classifier = classifier.Replace("$" + "{arch}", IntPtr.Size == 8 ? "64" : "32");
                        if (!string.IsNullOrEmpty(classifier))
                        {
                            var classifiers = lib["downloads"]?["classifiers"] as JObject;
                            var classToken = classifiers?[classifier];
                            if (classToken != null)
                            {
                                string path = classToken["path"]?.ToString() ?? "";
                                string url = classToken["url"]?.ToString() ?? "";
                                if (!string.IsNullOrEmpty(path))
                                {
                                    if (_downloadSource != "Official")
                                    {
                                        url = url.Replace("https://libraries.minecraft.net", _downloadSource + "/maven");
                                    }
                                    downloads.Add(new DownloadRequest
                                    {
                                        Url = url,
                                        DestinationPath = Path.Combine(_gamePath, "libraries", path),
                                        Sha1 = classToken["sha1"]?.ToString()
                                    });
                                }
                            }
                        }
                    }
                }
            }

            // 6. Assets
            var assetIndex = versionJson["assetIndex"];
            if (assetIndex != null)
            {
                string indexId = assetIndex["id"]?.ToString() ?? "";
                string indexUrl = assetIndex["url"]?.ToString() ?? "";
                string indexSha1 = assetIndex["sha1"]?.ToString() ?? "";
                
                if (_downloadSource != "Official")
                {
                   indexUrl = indexUrl.Replace("https://launchermeta.mojang.com", _downloadSource);
                   indexUrl = indexUrl.Replace("https://piston-meta.mojang.com", _downloadSource);
                }

                string indexPath = Path.Combine(_gamePath, "assets", "indexes", $"{indexId}.json");
                await _downloadService.DownloadFileAsync(indexUrl, indexPath, indexSha1);

                // Parse Index
                var indexJson = JObject.Parse(File.ReadAllText(indexPath));
                var objects = indexJson["objects"] as JObject;
                if (objects != null)
                {
                    foreach (var obj in objects)
                    {
                        var hash = obj.Value?["hash"]?.ToString();
                        if (string.IsNullOrEmpty(hash)) continue;
                        
                        string twoPrefix = hash.Substring(0, 2);
                        string assetUrl = _downloadSource == "Official" 
                            ? $"https://resources.download.minecraft.net/{twoPrefix}/{hash}"
                            : $"{_downloadSource}/assets/{twoPrefix}/{hash}";
                            
                        string assetPath = Path.Combine(_gamePath, "assets", "objects", twoPrefix, hash);
                        
                        downloads.Add(new DownloadRequest { Url = assetUrl, DestinationPath = assetPath, Sha1 = hash });
                    }
                }
            }

            // 7. Execute Batch Download
            await _downloadService.DownloadBatchAsync(downloads, progress);
        }

        private bool IsLibraryAllowed(JToken lib)
        {
            var rules = lib["rules"] as JArray;
            if (rules == null) return true;

            bool isAllowed = true;

            foreach (var rule in rules)
            {
                var action = rule["action"]?.ToString();
                var os = rule["os"];
                var features = rule["features"];

                if (features != null) continue;

                if (os == null)
                {
                    isAllowed = (action == "allow");
                }
                else
                {
                    string osName = os["name"]?.ToString() ?? "";
                    if (string.IsNullOrEmpty(osName) || osName.Equals("windows", StringComparison.OrdinalIgnoreCase))
                    {
                        string arch = os["arch"]?.ToString() ?? "";
                        bool archMatch = true;
                        if (!string.IsNullOrEmpty(arch))
                        {
                            bool is64 = IntPtr.Size == 8;
                            if (arch == "x86" && is64) archMatch = false;
                            if ((arch == "x86_64" || arch == "amd64") && !is64) archMatch = false;
                        }

                        if (archMatch)
                        {
                            isAllowed = (action == "allow");
                        }
                    }
                }
            }

            return isAllowed;
        }
    }
}
