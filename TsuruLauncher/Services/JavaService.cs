using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace TsuruLauncher.Services
{
    public class JavaInstallation
    {
        public string Path { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public bool Is64Bit { get; set; } = true;

        public override string ToString() => $"{Version} ({Path})";
    }

    public class JavaService
    {
        private static List<JavaInstallation>? _cachedInstallations;
        private static DateTime _cacheExpiryTime = DateTime.MinValue;
        private const int CacheDurationMinutes = 30;

        public List<JavaInstallation> FindInstallations()
        {
            // Check if cache is valid
            if (_cachedInstallations != null && DateTime.Now < _cacheExpiryTime)
            {
                return _cachedInstallations;
            }

            var installations = new List<JavaInstallation>();
            var scannedPaths = new HashSet<string>();

            // 1. Scan Registry
            ScanRegistryKey(Registry.LocalMachine.OpenSubKey(@"SOFTWARE\JavaSoft\Java Runtime Environment"), installations, scannedPaths);
            ScanRegistryKey(Registry.LocalMachine.OpenSubKey(@"SOFTWARE\JavaSoft\JDK"), installations, scannedPaths);
            ScanRegistryKey(Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\JavaSoft\Java Runtime Environment"), installations, scannedPaths);

            // 2. Scan Common Paths
            var commonPaths = new[]
            {
                @"C:\Program Files\Java",
                @"C:\Program Files (x86)\Java",
                @"C:\Program Files\Eclipse Adoptium",
                @"C:\Program Files\Microsoft\jdk",
                @"C:\Program Files\Azul\zulu",
                @"C:\Program Files\BellSoft\LibericaJDK",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eclipse Adoptium"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jdks"), // IntelliJ
                JavaRuntimeService.RuntimeRoot // runtimes downloaded by the launcher itself
            };

            foreach (var basePath in commonPaths)
            {
                if (Directory.Exists(basePath))
                {
                    try
                    {
                        var javaExecutables = Directory.GetFiles(basePath, "javaw.exe", SearchOption.AllDirectories);
                        foreach (var exec in javaExecutables)
                        {
                            if (scannedPaths.Contains(exec)) continue;
                            
                            var info = GetJavaInfo(exec);
                            if (info != null)
                            {
                                installations.Add(info);
                                scannedPaths.Add(exec);
                            }
                        }
                    }
                    catch { }
                }
            }

            // Cache the results
            _cachedInstallations = installations.OrderByDescending(j => j.Version).ToList();
            _cacheExpiryTime = DateTime.Now.AddMinutes(CacheDurationMinutes);

            return _cachedInstallations;
        }

        private void ScanRegistryKey(RegistryKey? key, List<JavaInstallation> installations, HashSet<string> scannedPaths)
        {
            if (key == null) return;
            try
            {
                foreach (var ver in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(ver);
                    var javaHome = subKey?.GetValue("JavaHome")?.ToString();
                    if (!string.IsNullOrEmpty(javaHome))
                    {
                        var execPath = Path.Combine(javaHome, "bin", "javaw.exe");
                        if (File.Exists(execPath) && !scannedPaths.Contains(execPath))
                        {
                            var info = GetJavaInfo(execPath);
                            if (info != null)
                            {
                                installations.Add(info);
                                scannedPaths.Add(execPath);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public JavaInstallation? GetJavaInfo(string path)
        {
            try
            {
                var fileInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                
                // Heuristic for version if FileVersion is not standard
                string version = fileInfo.ProductVersion ?? fileInfo.FileVersion ?? "Unknown";
                
                // Try to parse standard java versions like 1.8.0_20, 17.0.1, etc.
                // For now, just return what we have
                
                return new JavaInstallation
                {
                    Path = path,
                    Version = version,
                    Is64Bit = path.Contains("Program Files") && !path.Contains("x86") // naive check
                };
            }
            catch
            {
                return null;
            }
        }

        public string? AutoDetectBestJava(int requiredVersion = 0)
        {
            var installs = FindInstallations()
                .Where(j => !string.IsNullOrEmpty(j.Path) && File.Exists(j.Path))
                .ToList();
            if (!installs.Any()) return null;

            // Helper: order by major version desc, prefer 64-bit
            IEnumerable<JavaInstallation> OrderByQuality(IEnumerable<JavaInstallation> list) =>
                list.OrderByDescending(j => GetMajorVersion(j.Version))
                    .ThenByDescending(j => j.Is64Bit);

            if (requiredVersion > 0)
            {
                // 1. 精确主版本且 64 位
                var exact64 = OrderByQuality(
                    installs.Where(j => GetMajorVersion(j.Version) == requiredVersion && j.Is64Bit)
                ).FirstOrDefault();
                if (exact64 != null) return exact64.Path;

                // 2. 精确主版本（任意位数）
                var exactAny = OrderByQuality(
                    installs.Where(j => GetMajorVersion(j.Version) == requiredVersion)
                ).FirstOrDefault();
                if (exactAny != null) return exactAny.Path;

                // 3. 对于需要 Java 8 的老版本，兼容各种 1.8/8.x 标记
                if (requiredVersion == 8)
                {
                    var legacy8 = OrderByQuality(
                        installs.Where(j =>
                        {
                            var major = GetMajorVersion(j.Version);
                            return major == 8 ||
                                   j.Version.StartsWith("1.8", StringComparison.OrdinalIgnoreCase) ||
                                   j.Version.StartsWith("8.", StringComparison.OrdinalIgnoreCase);
                        })
                    ).FirstOrDefault();
                    if (legacy8 != null) return legacy8.Path;
                }

                // 4. 对于需要 17/21 等高版本，选 >= required 的，优先 64 位
                var high = OrderByQuality(
                    installs.Where(j => GetMajorVersion(j.Version) >= requiredVersion)
                ).FirstOrDefault();
                if (high != null) return high.Path;

                // 5. 明确要求了版本却找不到满足的：返回 null，让调用方给出
                //    "请安装 Java X" 的提示。绝不回退到更低的版本——那只会让
                //    JVM 因不支持的参数/字节码崩溃（例如用 Java 8 启动 1.20.5+）。
                return null;
            }

            // 6. 未指定版本要求：选择最高版本，优先 64 位
            return OrderByQuality(installs).First().Path;
        }

        public JavaInstallation? GetJavaFromPath(string path)
        {
             if (File.Exists(path)) return GetJavaInfo(path);
             return null;
        }

        public int GetMajorVersion(string version)
        {
            try
            {
                if (version.StartsWith("1."))
                {
                    // 1.8.0_202 -> 8
                    return int.Parse(version.Split('.')[1]);
                }
                else
                {
                    // 17.0.1 -> 17
                    return int.Parse(version.Split('.')[0]);
                }
            }
            catch
            {
                return 0;
            }
        }

        public void ClearCache()
        {
            _cachedInstallations = null;
            _cacheExpiryTime = DateTime.MinValue;
        }
    }
}