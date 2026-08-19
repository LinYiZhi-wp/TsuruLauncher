using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using GeminiLauncher.Models;

namespace GeminiLauncher.Services
{
    public class GameService
    {
        private readonly ConfigService _configService;

        public GameService(ConfigService configService)
        {
            _configService = configService;
        }

        public List<GameInstance> ScanVersions(string dotMinecraftPath, bool isIsolationEnabled)
        {
            var versionsDir = Path.Combine(dotMinecraftPath, "versions");

            if (!Directory.Exists(versionsDir))
                return new List<GameInstance>();

            var directories = Directory.GetDirectories(versionsDir);
            var results = new List<GameInstance>();

            foreach (var dir in directories)
            {
                var dirName = new DirectoryInfo(dir).Name;
                var jsonPath = Path.Combine(dir, $"{dirName}.json");

                if (File.Exists(jsonPath))
                {
                    try
                    {
                        var instance = ParseVersionJson(jsonPath, dotMinecraftPath, isIsolationEnabled);
                        if (instance != null)
                        {
                            results.Add(instance);
                        }
                    }
                    catch (Exception)
                    {
                    }
                }
            }

            return results;
        }

        public GameInstance? ParseVersionJson(string jsonPath, string dotMinecraftPath, bool isIsolationEnabled)
        {
            try
            {
                string jsonContent = File.ReadAllText(jsonPath);
                JObject json = JObject.Parse(jsonContent);

                string id = json["id"]?.ToString() ?? "";
                string? versionDir = Path.GetDirectoryName(jsonPath);
                if (string.IsNullOrEmpty(versionDir)) return null;

                // Parse Downloads (Client)
                string clientJarUrl = "";
                string clientJarSha1 = "";
                long clientJarSize = 0;

                var downloads = json["downloads"];
                if (downloads != null)
                {
                    var client = downloads["client"];
                    if (client != null)
                    {
                        clientJarUrl = client["url"]?.ToString() ?? "";
                        clientJarSha1 = client["sha1"]?.ToString() ?? "";
                        clientJarSize = (long)(client["size"] ?? 0);
                    }
                }

                // Handle Inheritance (Recursion)
                GameInstance instance;
                if (json.ContainsKey("inheritsFrom"))
                {
                    string parentId = json["inheritsFrom"]?.ToString() ?? "";
                    string parentDir = Path.Combine(dotMinecraftPath, "versions", parentId);
                    string parentJson = Path.Combine(parentDir, $"{parentId}.json");
                    
                    if (File.Exists(parentJson))
                    {
                        // Recursively parse parent
                        instance = ParseVersionJson(parentJson, dotMinecraftPath, isIsolationEnabled) ?? new GameInstance();
                        // Update ID and specific paths to the child version
                        instance.Id = id;
                        instance.GameDir = isIsolationEnabled ? versionDir : dotMinecraftPath; 
                        // Note: If parent was isolated, it might set GameDir to parentDir. We overwrite it here if child is isolated.
                        // Actually, let's re-evaluate isolation for the child:
                        bool isIsolated = isIsolationEnabled || 
                                          Directory.Exists(Path.Combine(versionDir, "mods")) || 
                                          File.Exists(Path.Combine(versionDir, "options.txt"));
                        instance.GameDir = isIsolated ? versionDir : dotMinecraftPath;
                    }
                    else
                    {
                        // Parent missing? Fallback to bare instance (might crash later)
                        instance = new GameInstance { Id = id, RootPath = dotMinecraftPath };
                    }
                }
                else
                {
                    // Base version
                    bool isIsolated = isIsolationEnabled || 
                                      Directory.Exists(Path.Combine(versionDir, "mods")) || 
                                      File.Exists(Path.Combine(versionDir, "options.txt"));

                    instance = new GameInstance
                    {
                        Id = id,
                        RootPath = dotMinecraftPath,
                        GameDir = isIsolated ? versionDir : dotMinecraftPath,
                        Type = json["type"]?.ToString() ?? "release"
                    };
                }

                // Assign parsed downloads
                if (!string.IsNullOrEmpty(clientJarUrl))
                {
                    instance.ClientJarUrl = clientJarUrl;
                    instance.ClientJarSha1 = clientJarSha1;
                    instance.FileSize = clientJarSize;
                }

                // Merge/Overwrite properties from current JSON
                // MainClass: Child overrides parent
                if (json["mainClass"] != null) instance.MainClass = json["mainClass"]?.ToString() ?? "";
                if (json["minecraftArguments"] != null) instance.MinecraftArguments = json["minecraftArguments"]?.ToString() ?? "";
                
                // Assets: Child overrides parent (usually)
                if (json["assetIndex"] != null) instance.AssetIndexId = json["assetIndex"]?["id"]?.ToString() ?? "legacy";
                
                int defaultJava = 8;
                try 
                {
                    // Detect default Java by MC version if missing in JSON
                    var versionParts = id.Split('.');
                    if (versionParts.Length >= 2 && int.TryParse(versionParts[1], out int minor))
                    {
                        if (minor > 21) defaultJava = 21; // future versions
                        else if (minor == 21) defaultJava = 21; // 1.21+
                        else if (minor == 20)
                        {
                            // 1.20.5+ requires Java 21; 1.20.0–1.20.4 requires Java 17
                            if (versionParts.Length >= 3 && int.TryParse(versionParts[2], out int patch) && patch >= 5)
                                defaultJava = 21;
                            else
                                defaultJava = 17;
                        }
                        else if (minor >= 18) defaultJava = 17; // 1.18+
                        else if (minor >= 17) defaultJava = 17; // 1.17 (16 actually, but 17 is standard now)
                    }
                } catch {}

                if (json["javaVersion"] != null) instance.RequiredJavaVersion = json["javaVersion"]?["majorVersion"]?.ToObject<int>() ?? defaultJava;
                else instance.RequiredJavaVersion = defaultJava;

                // Libraries: Append child libraries to parent libraries
                var libs = json["libraries"] as JArray;
                if (libs != null)
                {
                    foreach (var lib in libs)
                    {
                        var parsedLibs = ParseLibraries(lib);
                        foreach (var library in parsedLibs)
                        {
                            AddOrReplaceLibrary(instance, library);
                        }
                    }
                }

                // Arguments: Append/Merge
                var args = json["arguments"];
                if (args != null)
                {
                    ParseArguments(args, instance);
                }

                // Logging Config
                var logging = json["logging"];
                if (logging != null) 
                {
                   instance.LogConfig = ParseLogging(logging);
                }

                // Custom Config
                LoadVersionConfig(instance);

                return instance;
            }
            catch
            {
                return null;
            }
        }

        private LogConfig? ParseLogging(JToken loggingToken)
        {
            try
            {
                // Prioritize client logging
                var client = loggingToken["client"];
                if (client == null) return null;

                var file = client["file"];
                if (file == null) return null;

                return new LogConfig
                {
                    Argument = client["argument"]?.ToString() ?? "",
                    Type = client["type"]?.ToString() ?? "",
                    File = new LogFile
                    {
                        Id = file["id"]?.ToString() ?? "",
                        Sha1 = file["sha1"]?.ToString() ?? "",
                        Size = (long)(file["size"] ?? 0),
                        Url = file["url"]?.ToString() ?? ""
                    }
                };
            }
            catch
            {
                return null;
            }
        }

        private IEnumerable<Library> ParseLibraries(JToken libToken)
        {
            var name = libToken["name"]?.ToString();
            if (string.IsNullOrEmpty(name)) yield break;

            var parts = name.Split(':');
            var group = parts[0].Replace('.', '/');
            var artifact = parts[1];
            var version = parts[2];
            
            // 1. Process Main Artifact (Code Jar or specific classifier in name)
            // If name has classifier, it's specific. If not, it's the main jar.
            var mainClassifier = parts.Length > 3 ? parts[3] : "";
            
            var mainPath = $"{group}/{artifact}/{version}/{artifact}-{version}";
            if (!string.IsNullOrEmpty(mainClassifier)) mainPath += $"-{mainClassifier}";
            mainPath += ".jar";

            var downloads = libToken["downloads"];
            var artifactToken = downloads?["artifact"];
            
            // Determine if we should yield the main artifact
            // If explicit artifact exists OR no downloads section (legacy), we yield it.
            // BUT: If the JSON entry is PURELY for a native (unlikely with this structure, usually name has classifier), we yield it.
            // The issue was: we were overwriting this with the native classifier path.
            
            string mainUrl = artifactToken?["url"]?.ToString() ?? "";
            string mainSha1 = artifactToken?["sha1"]?.ToString() ?? "";
            if (artifactToken?["path"] != null) mainPath = artifactToken["path"]?.ToString() ?? mainPath;

            // Legacy fallback
             if (string.IsNullOrEmpty(mainUrl))
            {
                mainUrl = "https://bmclapi2.bangbang93.com/maven/" + mainPath;
            }
            
            // Apply Download Mirror
            if (_configService.Settings.DownloadSource == "BMCLAPI")
            {
                if (mainUrl.StartsWith("https://libraries.minecraft.net/"))
                {
                    mainUrl = mainUrl.Replace("https://libraries.minecraft.net/", "https://bmclapi2.bangbang93.com/maven/");
                }
            }
            

            // Yield Main Artifact
            yield return new Library
            {
                Name = name, // Keep original name
                Path = mainPath,
                Url = mainUrl,
                Checksum = mainSha1
            };

            // 2. Process Native Artifact (if applicable)
            var natives = libToken["natives"];
            if (natives != null)
            {
                string osName = "windows"; // TODO: dynamic
                var classifierNode = natives[osName];
                if (classifierNode != null)
                {
                    string cls = classifierNode.ToString().Replace("${arch}", IntPtr.Size == 8 ? "64" : "32");
                    
                    if (downloads?["classifiers"] is JObject classifiers && classifiers[cls] is JToken classToken)
                    {
                         var nativePath = $"{group}/{artifact}/{version}/{artifact}-{version}-{cls}.jar";
                         nativePath = classToken["path"]?.ToString() ?? nativePath;
                         
                         var nativeUrl = classToken["url"]?.ToString() ?? "";
                         var nativeSha1 = classToken["sha1"]?.ToString() ?? "";

                         if (!string.IsNullOrEmpty(nativeUrl))
                         {
                             if (_configService.Settings.DownloadSource == "BMCLAPI" && nativeUrl.StartsWith("https://libraries.minecraft.net/"))
                             {
                                 nativeUrl = nativeUrl.Replace("https://libraries.minecraft.net/", "https://bmclapi2.bangbang93.com/maven/");
                             }
                         }

                         // Construct a name that includes the classifier for identification
                         // Standard format: Group:Artifact:Version:Classifier
                         var nativeName = $"{parts[0]}:{parts[1]}:{parts[2]}:{cls}";

                         yield return new Library
                         {
                             Name = nativeName,
                             Path = nativePath,
                             Url = nativeUrl,
                             Checksum = nativeSha1,
                             IsNative = true // Optional flag, but valid
                         };
                    }
                }
            }
        }

        private void ParseArguments(JToken argsToken, GameInstance instance)
        {
            // 如果子版本提供了 game/jvm 字段，则根据内容决定是否覆盖父版本的参数，避免重复

            // 先处理 game 参数
            if (argsToken["game"] is JArray gameArgs)
            {
                if (ShouldOverrideGameArguments(gameArgs))
                {
                    // 子版本包含完整启动参数，占位符齐全：覆盖父版本
                    instance.GameArguments.Clear();
                }

                foreach (var item in gameArgs)
                {
                    if (item is JValue val)
                    {
                        instance.GameArguments.Add(new Argument { Value = val.ToString() });
                    }
                    else if (item is JObject obj)
                    {
                        // Handle rule-based arguments
                        if (CheckRules(obj["rules"] as JArray))
                        {
                            var value = obj["value"];
                            if (value is JArray valArray)
                            {
                                foreach (var v in valArray)
                                    instance.GameArguments.Add(new Argument { Value = v.ToString() });
                            }
                            else if (value != null)
                            {
                                instance.GameArguments.Add(new Argument { Value = value.ToString() });
                            }
                        }
                    }
                }
            }

            // 再处理 jvm 参数：子版本一旦提供 jvm 字段，则直接覆盖父版本，避免重复 -cp/-D 等
            if (argsToken["jvm"] is JArray jvmArgs)
            {
                instance.JvmArguments.Clear();

                foreach (var item in jvmArgs)
                {
                    if (item is JValue val)
                    {
                        var valueStr = val.ToString();
                        if (!IsProblematicJvmArg(valueStr))
                        {
                            instance.JvmArguments.Add(new Argument { Value = valueStr });
                        }
                    }
                    else if (item is JObject obj)
                    {
                        // Handle rule-based arguments (e.g., -XX:HeapDumpPath for windows)
                        if (CheckRules(obj["rules"] as JArray))
                        {
                            var value = obj["value"];
                            if (value is JArray valArray)
                            {
                                foreach (var v in valArray)
                                {
                                    var valueStr = v.ToString();
                                    if (!IsProblematicJvmArg(valueStr))
                                    {
                                        instance.JvmArguments.Add(new Argument { Value = valueStr });
                                    }
                                }
                            }
                            else if (value != null)
                            {
                                var valueStr = value.ToString();
                                if (!IsProblematicJvmArg(valueStr))
                                {
                                    instance.JvmArguments.Add(new Argument { Value = valueStr });
                                }
                            }
                        }
                    }
                }
            }
        }

        // 判断子版本的 game 参数是否应当覆盖父版本
        // 简单策略：如果包含核心占位符（如 ${auth_player_name} / ${version_name} 等），认为是完整参数，直接覆盖
        private bool ShouldOverrideGameArguments(JArray gameArgs)
        {
            foreach (var item in gameArgs)
            {
                string? text = null;
                if (item is JValue v)
                {
                    text = v.ToString();
                }
                else if (item is JObject obj)
                {
                    var value = obj["value"];
                    if (value is JValue sv)
                        text = sv.ToString();
                    else if (value is JArray arr && arr.Count > 0)
                        text = arr[0]?.ToString();
                }

                if (string.IsNullOrEmpty(text)) continue;

                if (text.Contains("${auth_player_name}") ||
                    text.Contains("${version_name}") ||
                    text.Contains("${game_directory}") ||
                    text.Contains("${assets_root}"))
                {
                    return true;
                }
            }

            return false;
        }

        // 过滤可能导致命令行解析异常的 JVM 参数
        private bool IsProblematicJvmArg(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            // 典型问题：-Dos.name=Windows 10 会把 “10” 当成主类
            if (value.StartsWith("-Dos.name=", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private bool CheckRules(JArray? rules)
        {
            // 规则为空 => 默认允许
            if (rules == null || rules.Count == 0) return true;
            
            bool allowed = false;

            foreach (var rule in rules)
            {
                string action = rule["action"]?.ToString() ?? "allow";
                var os = rule["os"];
                var features = rule["features"];
                
                bool match = true;

                // OS 匹配：目前仅区分 windows，其它 OS 的规则不会应用到当前环境
                if (os != null)
                {
                    string osName = os["name"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(osName) && !osName.Equals("windows", StringComparison.OrdinalIgnoreCase))
                    {
                        match = false;
                    }

                    // 简单 arch 匹配（如需要可以扩展）
                    string arch = os["arch"]?.ToString() ?? "";
                    if (!string.IsNullOrEmpty(arch))
                    {
                        bool is64 = IntPtr.Size == 8;
                        if (arch == "x86" && is64) match = false;
                        if ((arch == "x86_64" || arch == "amd64") && !is64) match = false;
                    }
                }

                // features: Check feature flags like is_demo_user
                if (features != null)
                {
                    // Default feature flags for now
                    bool isDemoUser = false; // TODO: Get from account status
                    bool hasCustomResolution = false; // TODO: Get from settings

                    foreach (var property in features.Cast<JProperty>())
                    {
                        bool requiredState = (bool)property.Value;
                        bool actualState = false;

                        if (property.Name == "is_demo_user") actualState = isDemoUser;
                        else if (property.Name == "has_custom_resolution") actualState = hasCustomResolution;
                        
                        // If required state matches actual state, rule applies (match = true).
                        // If not, rule does not apply (match = false).
                        if (actualState != requiredState)
                        {
                            match = false;
                            break;
                        }
                    }
                }

                if (!match) continue;

                // With match confirmed, apply the action
                if (action == "allow") allowed = true;
                else if (action == "disallow") allowed = false;
            }

            return allowed;
        }

        public void LoadVersionConfig(GameInstance instance)
        {
            try
            {
                string configPath = Path.Combine(instance.GameDir, "lyzl_profile.json");
                if (File.Exists(configPath))
                {
                    var json = JObject.Parse(File.ReadAllText(configPath));
                    if (json.ContainsKey("UseGlobalSettings")) instance.UseGlobalSettings = (bool?)json["UseGlobalSettings"] ?? false;
                    if (json.ContainsKey("CustomMemoryMb")) instance.CustomMemoryMb = (int?)json["CustomMemoryMb"] ?? 0;
                    if (json.ContainsKey("CustomJavaPath")) instance.CustomJavaPath = json["CustomJavaPath"]?.ToString() ?? "";
                    if (json.ContainsKey("CustomJvmArgs")) instance.CustomJvmArgs = json["CustomJvmArgs"]?.ToString() ?? "";
                }
            }
            catch {}
        }

        public void SaveVersionConfig(GameInstance instance)
        {
            try
            {
                var json = new JObject();
                json["UseGlobalSettings"] = instance.UseGlobalSettings;
                json["CustomMemoryMb"] = instance.CustomMemoryMb;
                json["CustomJavaPath"] = instance.CustomJavaPath;
                json["CustomJvmArgs"] = instance.CustomJvmArgs;

                string configPath = Path.Combine(instance.GameDir, "lyzl_profile.json");
                File.WriteAllText(configPath, json.ToString());
            }
            catch {}
        }
        private void AddOrReplaceLibrary(GameInstance instance, Library newLib)
        {
            // Calculate Identity: Group:Artifact[:Classifier]
            // Name format: Group:Artifact:Version[:Classifier]
            var parts = newLib.Name.Split(':');
            if (parts.Length < 3) 
            {
                // Fallback: just add if format is weird
                instance.Libraries.Add(newLib);
                return;
            }

            string identity = $"{parts[0]}:{parts[1]}";
            if (parts.Length > 3) identity += $":{parts[3]}";

            // Find existing library with same identity
            var existing = instance.Libraries.FirstOrDefault(l => 
            {
                var p = l.Name.Split(':');
                if (p.Length < 3) return false;
                string id = $"{p[0]}:{p[1]}";
                if (p.Length > 3) id += $":{p[3]}";
                return id == identity;
            });

            if (existing != null)
            {
                // Remove existing (older/parent) version
                instance.Libraries.Remove(existing);
            }

            // Add new (newer/child) version
            instance.Libraries.Add(newLib);
        }

    }
}
