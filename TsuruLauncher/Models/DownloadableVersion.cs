using System;

namespace TsuruLauncher.Models
{
    public class DownloadableVersion
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = "release";
        public string Url { get; set; } = string.Empty;
        public DateTime ReleaseTime { get; set; }
        public DateTime Time { get; set; }
        
        // Helper property for UI display
        public string DisplayType => Type switch
        {
            "release" => "正式版",
            "snapshot" => "快照版",
            "old_beta" => "远古测试版",
            "old_alpha" => "远古Alpha",
            _ => Type
        };

        public bool IsRelease => Type == "release";
    }

    public class VersionManifest
    {
        public DownloadableVersion? Latest { get; set; }
        public System.Collections.Generic.List<DownloadableVersion> Versions { get; set; } = new System.Collections.Generic.List<DownloadableVersion>();
    }
}