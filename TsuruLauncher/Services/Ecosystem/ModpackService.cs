using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TsuruLauncher.Models;
using TsuruLauncher.Services.Network;
using Newtonsoft.Json.Linq;

namespace TsuruLauncher.Services.Ecosystem
{
    public class ModpackService
    {
        private readonly DownloadService _downloadService;
        private readonly ModLoaderService _modLoaderService;

        public ModpackService()
        {
            _downloadService = new DownloadService();
            _modLoaderService = new ModLoaderService();
        }

        public async Task ImportMrPackAsync(string filePath, string dotMinecraftPath, IProgress<double>? progress = null, IProgress<string>? status = null)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException("Modpack file not found.", filePath);

            string tempDir = Path.Combine(Path.GetTempPath(), "Tsuru_Import_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            try
            {
                status?.Report("Extracting package...");
                progress?.Report(0);

                // 1. Extract .mrpack
                ZipFile.ExtractToDirectory(filePath, tempDir);

                // 2. Read modrinth.index.json
                string indexFile = Path.Combine(tempDir, "modrinth.index.json");
                if (!File.Exists(indexFile)) throw new Exception("Invalid .mrpack: modrinth.index.json missing.");

                var json = JObject.Parse(File.ReadAllText(indexFile));
                string packName = json["name"]?.ToString() ?? "Unknown Modpack";
                string safeName = string.Join("_", packName.Split(Path.GetInvalidFileNameChars()));

                status?.Report($"Preparing {packName}...");

                // Setup version directory
                string versionDir = Path.Combine(dotMinecraftPath, "versions", safeName);
                if (Directory.Exists(versionDir))
                {
                    safeName += "_" + DateTime.Now.Ticks;
                    versionDir = Path.Combine(dotMinecraftPath, "versions", safeName);
                }
                Directory.CreateDirectory(versionDir);

                // 3. Handle Game/Loader Version
                var dependencies = json["dependencies"];
                string minecraftVer = dependencies?["minecraft"]?.ToString() ?? "";
                string fabricVer = dependencies?["fabric-loader"]?.ToString() ?? "";
                // ... (rest of version logic) ...
                
                string baseVersionId = minecraftVer;
                JObject? versionJson = null;

                if (!string.IsNullOrEmpty(fabricVer))
                {
                    status?.Report($"Installing Fabric Loader {fabricVer}...");
                    if (string.IsNullOrEmpty(minecraftVer)) throw new Exception("Minecraft version not found in modpack.");
                    // Install Fabric and get the profile JSON
                    // This downloads fabric libraries and returns the JObject for the version
                    versionJson = await _modLoaderService.InstallFabricAsync(minecraftVer, fabricVer, dotMinecraftPath, progress, status);
                    
                    // Modify the ID to match our modpack
                    versionJson["id"] = safeName;
                }
                else
                {
                    // Fallback or Vanilla
                    versionJson = new JObject();
                    versionJson["id"] = safeName;
                    versionJson["inheritsFrom"] = minecraftVer;
                    versionJson["type"] = "release";
                    // For vanilla modpacks, we assume client.jar or inheritance.
                    // But since GameService doesn't support inheritance fully yet, this might need work for Vanilla packs.
                    // However, most .mrpacks are modded.
                } 
                
                File.WriteAllText(Path.Combine(versionDir, $"{safeName}.json"), versionJson.ToString());

                // 4. Download Files
                string modsDir = Path.Combine(versionDir, "mods");
                Directory.CreateDirectory(modsDir);

                var files = json["files"] as JArray;
                if (files != null && files.Count > 0)
                {
                    int totalFiles = files.Count;
                    int currentFile = 0;

                    foreach (var file in files)
                    {
                         currentFile++;
                         progress?.Report((double)currentFile / totalFiles); // 0..1, consistent with InstallFabricAsync

                         string downloadUrl = file["downloads"]?[0]?.ToString() ?? "";
                         string rawPath = file["path"]?.ToString() ?? "";
                         if (string.IsNullOrEmpty(downloadUrl) || string.IsNullOrEmpty(rawPath)) continue;

                         string fileName = Path.GetFileName(rawPath);
                         status?.Report($"Downloading {fileName} ({currentFile}/{totalFiles})...");

                         // Security: never allow pack-supplied paths to escape the version directory
                         string safeRel = rawPath.Replace('\\', '/');
                         string destPath = Path.GetFullPath(Path.Combine(versionDir, safeRel));
                         string versionRoot = Path.GetFullPath(versionDir) + Path.DirectorySeparatorChar;
                         if (!destPath.StartsWith(versionRoot, StringComparison.OrdinalIgnoreCase))
                         {
                             status?.Report($"跳过非法路径: {rawPath}");
                             continue;
                         }

                         string? destDir = Path.GetDirectoryName(destPath);
                         if (destDir != null && !Directory.Exists(destDir)) Directory.CreateDirectory(destDir);

                         await _downloadService.DownloadFileAsync(downloadUrl, destPath, null);
                    }
                }

                // 5. Copy Overrides
                string overridesDir = Path.Combine(tempDir, "overrides");
                if (Directory.Exists(overridesDir))
                {
                    if (overridesDir != null) CopyDirectory(overridesDir, versionDir, true);
                }
                
                // Copy overrides/mods if any separate
                // Some packs use "client-overrides"
                string clientOverrides = Path.Combine(tempDir, "client-overrides");
                 if (Directory.Exists(clientOverrides))
                {
                    if (clientOverrides != null) CopyDirectory(clientOverrides, versionDir, true);
                }

            }
            finally
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static void CopyDirectory(string sourceDir, string destinationDir, bool recursive)
        {
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists) throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

            DirectoryInfo[] dirs = dir.GetDirectories();
            Directory.CreateDirectory(destinationDir);

            foreach (FileInfo file in dir.GetFiles())
            {
                string targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, true);
            }

            if (recursive)
            {
                foreach (DirectoryInfo subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true);
                }
            }
        }
        public async Task ExportMrPackAsync(GameInstance instance, string outputPath)
        {
            if (instance == null || string.IsNullOrEmpty(instance.GameDir)) 
                throw new ArgumentException("Invalid game instance.");

            string tempDir = Path.Combine(Path.GetTempPath(), "Tsuru_Export_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            try
            {
                // 1. Prepare Structures
                var indexJson = new JObject();
                indexJson["formatVersion"] = 1;
                indexJson["game"] = "minecraft";
                indexJson["versionId"] = "1.0.0";
                indexJson["name"] = instance.Id;
                indexJson["summary"] = "Exported by Tsuru Launcher";
                
                var dependencies = new JObject();

                // Detect the MC version from the version id ("1.20.1-forge-47.2.0" -> "1.20.1")
                string mcVersion = Regex.Match(instance.Id, @"^\d+\.\d+(\.\d+)?").Value;
                if (string.IsNullOrEmpty(mcVersion)) mcVersion = instance.Id.Split('-')[0];
                dependencies["minecraft"] = mcVersion;

                // Detect the mod loader from the version id
                string lowerId = instance.Id.ToLowerInvariant();
                if (lowerId.Contains("fabric"))
                    dependencies["fabric-loader"] = ExtractLoaderVersion(instance.Id, "fabric") ?? "latest";
                else if (lowerId.Contains("quilt"))
                    dependencies["quilt-loader"] = ExtractLoaderVersion(instance.Id, "quilt") ?? "latest";
                else if (lowerId.Contains("neoforge"))
                    dependencies["neoforge"] = ExtractLoaderVersion(instance.Id, "neoforge") ?? "latest";
                else if (lowerId.Contains("forge"))
                    dependencies["forge"] = ExtractLoaderVersion(instance.Id, "forge") ?? "latest";

                indexJson["dependencies"] = dependencies;

                var filesArray = new JArray();
                var modrinthService = new ModrinthService();
                
                string modsDir = Path.Combine(instance.GameDir, "mods");
                if (Directory.Exists(modsDir))
                {
                    foreach (var file in Directory.GetFiles(modsDir, "*.jar"))
                    {
                        // Calculate Hash (SHA1 for lookup, SHA512 for index)
                        string sha1 = ComputeHash(file, System.Security.Cryptography.SHA1.Create());
                        string sha512 = ComputeHash(file, System.Security.Cryptography.SHA512.Create());

                        // Lookup on Modrinth
                        var modInfo = await modrinthService.GetVersionByHashAsync(sha1);

                        if (modInfo != null)
                        {
                            // It's a Modrinth mod! Add to index.
                            var fileObj = new JObject();
                            fileObj["path"] = "mods/" + Path.GetFileName(file);
                            
                            var hashes = new JObject();
                            hashes["sha1"] = sha1;
                            hashes["sha512"] = sha512;
                            fileObj["hashes"] = hashes;

                            var env = new JObject();
                            env["client"] = "required";
                            env["server"] = "required"; // Default
                            fileObj["env"] = env;

                            var downloads = new JArray();
                            downloads.Add(modInfo.DownloadUrl);
                            fileObj["downloads"] = downloads;
                            
                            fileObj["fileSize"] = new FileInfo(file).Length;

                            filesArray.Add(fileObj);
                        }
                        else
                        {
                            // It's a custom jar (overrides)
                            string relPath = "overrides/mods/" + Path.GetFileName(file);
                            string destPath = Path.Combine(tempDir, "overrides", "mods", Path.GetFileName(file));
                            string? destDir = Path.GetDirectoryName(destPath);
                            if (destDir != null) Directory.CreateDirectory(destDir);
                            File.Copy(file, destPath);
                        }
                    }
                }
                indexJson["files"] = filesArray;

                // 2. Process Configs (Add to overrides)
                string configDir = Path.Combine(instance.GameDir, "config");
                if (Directory.Exists(configDir))
                {
                    CopyDirectory(configDir, Path.Combine(tempDir, "overrides", "config"), true);
                }
                
                // 3. Write index
                File.WriteAllText(Path.Combine(tempDir, "modrinth.index.json"), indexJson.ToString());

                // 4. Zip it up
                if (File.Exists(outputPath)) File.Delete(outputPath);
                ZipFile.CreateFromDirectory(tempDir, outputPath);
            }
            finally
            {
                 try { Directory.Delete(tempDir, true); } catch { }
            }
        }

        private static string? ExtractLoaderVersion(string versionId, string loader)
        {
            var parts = versionId.Split('-');
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Equals(loader, StringComparison.OrdinalIgnoreCase) && i + 1 < parts.Length)
                    return parts[i + 1];
            }
            return null;
        }

        private string ComputeHash(string filePath, System.Security.Cryptography.HashAlgorithm algorithm)
        {
            using (var stream = File.OpenRead(filePath))
            {
                byte[] hash = algorithm.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}