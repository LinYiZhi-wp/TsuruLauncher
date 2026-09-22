using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Collections.Generic;
using TsuruLauncher.Models;

namespace TsuruLauncher.Services
{
    public class GameExportService
    {
        public static async Task ExportGameAsync(GameInstance game, ExportOptions options, IProgress<double>? progress)
        {
            if (string.IsNullOrEmpty(options.ExportPath))
                throw new ArgumentException("Export path cannot be empty.");

            // Run in background task
            await Task.Run(() =>
            {
                // Ensure output directory exists (if not creating zip directly)
                // We are creating a zip file directly.
                string zipPath = options.ExportPath;
                if (File.Exists(zipPath)) File.Delete(zipPath);

                using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    // 1. Export Game Core (.minecraft/versions/{id})
                    if (options.IncludeGameCore)
                    {
                        string versionDir = Path.Combine(game.RootPath, "versions", game.Id);
                        if (Directory.Exists(versionDir))
                        {
                            AddDirectoryToZip(zip, versionDir, $"versions/{game.Id}");
                        }
                        
                        // Also include libraries? Usually not for simple modpack export, but useful for offline packs.
                        // PCL usually exports a lightweight pack focusing on overrides.
                        // We will skip libraries for now unless explicitly requested (which is not in UI).
                    }

                    // 2. Export Configs & Mods (Usually in GameDir or .minecraft if not isolated)
                    // If version isolation is used, GameDir is .minecraft/versions/{id}
                    // If not, GameDir is .minecraft
                    // We need to check if game uses isolation.
                    // Assuming GameDir IS the working directory.
                    string gameDir = game.GameDir;
                    if (string.IsNullOrEmpty(gameDir)) gameDir = game.RootPath; // Fallback

                    // Mods
                    if (options.IncludeMods)
                    {
                        string modsDir = Path.Combine(gameDir, "mods");
                        if (Directory.Exists(modsDir))
                        {
                            AddDirectoryToZip(zip, modsDir, "mods"); // If isolation, it goes to root of zip? or versions/{id}/mods?
                            // Standard modpack structure usually puts mods at root of zip override.
                        }
                    }

                    // Configs
                    if (options.IncludeGameSettings)
                    {
                        string configDir = Path.Combine(gameDir, "config");
                        if (Directory.Exists(configDir))
                        {
                            AddDirectoryToZip(zip, configDir, "config");
                        }
                        
                        // options.txt
                        string optionsFile = Path.Combine(gameDir, "options.txt");
                        if (File.Exists(optionsFile))
                        {
                            zip.CreateEntryFromFile(optionsFile, "options.txt");
                        }
                    }

                    // 3. Saves
                    if (options.IncludeSaves)
                    {
                        string savesDir = Path.Combine(gameDir, "saves");
                        if (Directory.Exists(savesDir))
                        {
                            AddDirectoryToZip(zip, savesDir, "saves");
                        }
                    }

                    // 4. Resource Packs / Shader Packs
                    if (options.IncludeResourcePacks)
                    {
                        string rpDir = Path.Combine(gameDir, "resourcepacks");
                        if (Directory.Exists(rpDir))
                        {
                            AddDirectoryToZip(zip, rpDir, "resourcepacks");
                        }
                    }
                    if (options.IncludeShaderPacks)
                    {
                        string spDir = Path.Combine(gameDir, "shaderpacks");
                        if (Directory.Exists(spDir))
                        {
                            AddDirectoryToZip(zip, spDir, "shaderpacks");
                        }
                    }
                    
                    // 5. Manifest (Optional, for modpacks)
                    // We can create a simple manifest.json
                }
            });
        }

        private static void AddDirectoryToZip(ZipArchive zip, string sourceDir, string entryPrefix)
        {
            var files = Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                // Create relative path
                string relativePath = Path.GetRelativePath(sourceDir, file);
                string entryName = Path.Combine(entryPrefix, relativePath).Replace('\\', '/');
                zip.CreateEntryFromFile(file, entryName);
            }
        }
    }
}