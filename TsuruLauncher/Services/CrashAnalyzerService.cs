using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TsuruLauncher.Services
{
    public class CrashAnalyzerService
    {
        public class CrashResult
        {
            public string Cause { get; set; } = "Unknown";
            public string Solution { get; set; } = "Please check the logs for more details.";
            public string StackTrace { get; set; } = string.Empty;
            public bool IsCrashDetected { get; set; } = false;
        }

        public async Task<CrashResult> AnalyzeAsync(string gameDir)
        {
            var result = new CrashResult();

            // 1. Check for crash-reports folder
            string reportsDir = Path.Combine(gameDir, "crash-reports");
            FileInfo? latestReport = null;

            if (Directory.Exists(reportsDir))
            {
                var dirInfo = new DirectoryInfo(reportsDir);
                latestReport = dirInfo.GetFiles("crash-*.txt")
                                      .OrderByDescending(f => f.LastWriteTime)
                                      .FirstOrDefault();
            }

            // 2. Check latest.log if no crash report or if log is newer
            string logPath = Path.Combine(gameDir, "logs", "latest.log");
            string content = "";

            if (latestReport != null)
            {
                // If crash report is very fresh (less than 5 mins ago), use it
                if (DateTime.Now - latestReport.LastWriteTime < TimeSpan.FromMinutes(5))
                {
                    content = await File.ReadAllTextAsync(latestReport.FullName);
                    result.IsCrashDetected = true;
                }
            }

            if (string.IsNullOrEmpty(content) && File.Exists(logPath))
            {
                 content = await File.ReadAllTextAsync(logPath);
                 if (content.Contains("Minecraft has crashed!") ||
                     content.Contains("Stopping due to error") ||
                     content.Contains("A critical error occurred") ||
                     content.Contains("Process crashed with exit code"))
                 {
                     result.IsCrashDetected = true;
                 }
            }

            if (!result.IsCrashDetected) return result;

            // 3. Analyze Content
            AnalyzeContent(content, result);
            return result;
        }

        private void AnalyzeContent(string log, CrashResult result)
        {
            // Truncate for display if needed, but analyze full
            result.StackTrace = ExtractStackTrace(log);

            // Heuristics
            if (log.Contains("java.lang.OutOfMemoryError"))
            {
                result.Cause = "Out of Memory (RAM)";
                result.Solution = "Allocate more RAM in Settings. Try 4GB or more.";
            }
            else if (log.Contains("UnsupportedClassVersionError"))
            {
                result.Cause = "Java Version Mismatch";
                result.Solution = "You are using an older Java version for a newer game/mod.\n" +
                                  "- 1.18+ needs Java 17\n" +
                                  "- 1.17 needs Java 16\n" +
                                  "- 1.16 and older need Java 8";
            }
            else if (log.Contains("ClassNotFoundException") || log.Contains("NoClassDefFoundError"))
            {
                result.Cause = "Missing Dependency or Mod Conflict";
                result.Solution = "A mod is missing a required library or another mod.\n" +
                                  "Check the stack trace to identify the missing class.";
                
                // Try to extract class name
                var match = Regex.Match(log, @"Caused by: java\.lang\.ClassNotFoundException: ([\w\.]+)");
                if (match.Success)
                {
                    result.Solution += $"\nMissing Class: {match.Groups[1].Value}";
                }
            }
            else if (log.Contains("org.spongepowered.asm.mixin.transformer.throwables.MixinTransformerError"))
            {
                result.Cause = "Mixin Conflict (Mod Incompatibility)";
                result.Solution = "Two mods are trying to modify the same code. Try disabling recent mods.";
            }
            else if (log.Contains("Pixel format not accelerated"))
            {
                result.Cause = "Graphics Driver Issue";
                result.Solution = "Update your graphics card drivers. OpenGL is not supported.";
            }
        }

        private string ExtractStackTrace(string log)
        {
            // Find the first "Exception" or "Error" and take following lines
            int index = log.IndexOf("Exception in thread");
            if (index == -1) index = log.IndexOf("FATAL");
            if (index == -1) index = log.IndexOf("Error");
            
            if (index != -1)
            {
                return log.Substring(index, Math.Min(2000, log.Length - index)); // Limit to 2000 chars
            }
            return "No stack trace found in simple analysis.";
        }

        public Task<string?> CheckForConflictsAsync(string gameDir)
        {
            return Task.Run(() =>
            {
                string modsDir = Path.Combine(gameDir, "mods");
                if (!Directory.Exists(modsDir)) return (string?)null;

                var modFiles = Directory.GetFiles(modsDir, "*.jar");
                var lowerNames = modFiles
                    .Select(f => Path.GetFileName(f).ToLowerInvariant())
                    .ToList();

                // Known incompatibilities: check across the WHOLE mods folder —
                // the conflicting mods are separate files, so per-file checks never fire.
                bool hasOptifine = lowerNames.Any(n => n.Contains("optifine"));
                bool hasSodium = lowerNames.Any(n => n.Contains("sodium"));
                bool hasRubidium = lowerNames.Any(n => n.Contains("rubidium"));

                if (hasOptifine && hasSodium)
                    return "Conflict Detected: OptiFine and Sodium cannot be installed together.";

                if (hasRubidium && hasSodium)
                    return "Conflict Detected: Rubidium and Sodium are the same mod for different loaders.";

                return (string?)null;
            });
        }
    }
}