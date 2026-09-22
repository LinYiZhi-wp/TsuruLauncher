using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace TsuruLauncher.Services
{
    /// <summary>
    /// Lightweight metadata for a mod jar, extracted from the jar itself
    /// (fabric.mod.json / quilt.mod.json / META-INF/mods.toml) with a
    /// filename heuristic as fallback.
    /// </summary>
    public class ModJarInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string ModId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Loader { get; set; } = "未知"; // Fabric / Forge / NeoForge / Quilt / 未知
        public List<string> GameVersions { get; set; } = new();
        public bool IsDisabled { get; set; }

        public string LoaderIcon => Loader switch
        {
            "Fabric" => "🧵",
            "Forge" => "🔧",
            "NeoForge" => "🧊",
            "Quilt" => "🪡",
            _ => "📦"
        };

        public string DisplayNameOrFile =>
            !string.IsNullOrEmpty(DisplayName) ? DisplayName : Path.GetFileNameWithoutExtension(FileName);

        /// <summary>
        /// e.g. "1.20.1 · Fabric 0.15.11" / "全局共享 · 未知版本信息"
        /// </summary>
        public string DetailText
        {
            get
            {
                var parts = new List<string>();
                if (GameVersions.Count > 0)
                    parts.Add(string.Join("/", GameVersions.Take(2)));
                if (!string.IsNullOrEmpty(Loader))
                    parts.Add(Loader);
                if (!string.IsNullOrEmpty(Version))
                    parts.Add(Version);
                return parts.Count > 0 ? string.Join(" · ", parts) : "未知版本信息";
            }
        }
    }

    public static class ModJarInspector
    {
        /// <summary>
        /// Reads metadata from a mod jar. Never throws — falls back to filename heuristics.
        /// </summary>
        public static ModJarInfo Inspect(string jarPath, bool isDisabled = false)
        {
            string fileName = Path.GetFileName(jarPath);
            var info = new ModJarInfo { FileName = fileName, IsDisabled = isDisabled };

            try
            {
                using var zip = ZipFile.OpenRead(jarPath);

                var fabricEntry = zip.GetEntry("fabric.mod.json");
                if (fabricEntry != null)
                {
                    ParseFabricJson(fabricEntry, info);
                    return info;
                }

                var quiltEntry = zip.GetEntry("quilt.mod.json");
                if (quiltEntry != null)
                {
                    info.Loader = "Quilt";
                    ParseFabricJson(quiltEntry, info);
                    return info;
                }

                var tomlEntry = zip.GetEntry("META-INF/mods.toml");
                if (tomlEntry != null)
                {
                    ParseModsToml(tomlEntry, info);
                    return info;
                }
            }
            catch
            {
                // corrupted jar or permission issue — fall through to heuristics
            }

            ParseFromFileName(fileName, info);
            return info;
        }

        private static void ParseFabricJson(ZipArchiveEntry entry, ModJarInfo info)
        {
            try
            {
                using var reader = new StreamReader(entry.Open());
                var json = JObject.Parse(reader.ReadToEnd());
                info.ModId = json["id"]?.ToString() ?? "";
                info.DisplayName = json["name"]?.ToString() ?? info.ModId;
                info.Version = json["version"]?.ToString() ?? "";
                if (string.IsNullOrEmpty(info.Loader) || info.Loader == "未知") info.Loader = "Fabric";

                var depends = json["depends"];
                if (depends != null)
                {
                    var mc = depends["minecraft"];
                    if (mc != null && !string.IsNullOrEmpty(mc.ToString()))
                        info.GameVersions.Add(mc.ToString());
                }
            }
            catch { }
        }

        private static void ParseModsToml(ZipArchiveEntry entry, ModJarInfo info)
        {
            try
            {
                using var reader = new StreamReader(entry.Open());
                string text = reader.ReadToEnd();

                info.Loader = text.Contains("modLoader", StringComparison.OrdinalIgnoreCase)
                    ? (text.Contains("neoforge", StringComparison.OrdinalIgnoreCase) ? "NeoForge" : "Forge")
                    : "Forge";

                var modId = Regex.Match(text, @"modIds*=s*""([^""]+)""");
                if (modId.Success) info.ModId = modId.Groups[1].Value;

                var version = Regex.Match(text, @"versions*=s*""([^""]+)""");
                if (version.Success) info.Version = version.Groups[1].Value;

                var displayName = Regex.Match(text, @"displayNames*=s*""([^""]+)""");
                if (displayName.Success) info.DisplayName = displayName.Groups[1].Value;

                // [[dependencies.<modId>]] blocks that target minecraft carry versionRange
                foreach (Match block in Regex.Matches(text, @"[[dependencies.([^]]+)]](.*?)(?=[[|$)", RegexOptions.Singleline))
                {
                    if (Regex.IsMatch(block.Groups[2].Value, @"modIds*=s*""minecraft"""))
                    {
                        var vr = Regex.Match(block.Groups[2].Value, @"versionRanges*=s*""([^""]+)""");
                        if (vr.Success && !string.IsNullOrEmpty(vr.Groups[1].Value))
                            info.GameVersions.Add(vr.Groups[1].Value);
                    }
                }
            }
            catch { }
        }

        private static void ParseFromFileName(string fileName, ModJarInfo info)
        {
            string lower = fileName.ToLowerInvariant();

            if (lower.Contains("neoforge")) info.Loader = "NeoForge";
            else if (lower.Contains("fabric")) info.Loader = "Fabric";
            else if (lower.Contains("quilt")) info.Loader = "Quilt";
            else if (lower.Contains("forge")) info.Loader = "Forge";

            // MC versions look like 1.x / 1.x.y — pick the first one
            var mc = Regex.Match(fileName, @"(1.d+(.d+)?)");
            if (mc.Success) info.GameVersions.Add(mc.Groups[1].Value);

            // Display name = filename minus version-ish suffixes
            string name = Path.GetFileNameWithoutExtension(fileName);
            if (!string.IsNullOrEmpty(info.ModId)) info.DisplayName = info.ModId;
            else if (mc.Success)
                info.DisplayName = name.Replace(mc.Groups[1].Value, "").Trim('-', '_', ' ');
            else
                info.DisplayName = name;

            if (string.IsNullOrEmpty(info.ModId)) info.ModId = name;
        }
    }
}