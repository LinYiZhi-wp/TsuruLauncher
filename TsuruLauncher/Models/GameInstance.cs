namespace TsuruLauncher.Models
{
    public class GameInstance
    {
        public string Id { get; set; } = string.Empty; // e.g. "1.16.5"
        public string Type { get; set; } = "release"; // release, snapshot, old_beta...
        public string MainClass { get; set; } = string.Empty;
        public string RootPath { get; set; } = string.Empty; // The .minecraft folder (libraries/assets)
        public string GameDir { get; set; } = string.Empty; // The working directory (mods/saves)
        public int RequiredJavaVersion { get; set; } = 8; // Default to 8 for older versions
        public string MinecraftArguments { get; set; } = string.Empty; // For old versions (<1.13)
        public string AssetIndexId { get; set; } = string.Empty; // e.g. "1.16" or "legacy"
        public string ClientJarUrl { get; set; } = string.Empty;
        public string ClientJarSha1 { get; set; } = string.Empty;
        public long FileSize { get; set; } = 0;
        
        public LogConfig? LogConfig { get; set; }

        public List<Library> Libraries { get; set; } = new List<Library>();
        public List<string> InheritsFrom { get; set; } = new List<string>(); // For Forge/Fabric
        
        // For 1.13+ arguments
        public List<Argument> GameArguments { get; set; } = new List<Argument>();
        public List<Argument> JvmArguments { get; set; } = new List<Argument>();
        
        public bool UseGlobalSettings { get; set; } = true;
        public int CustomMemoryMb { get; set; } = 4096;
        public int CustomMinMemoryMb { get; set; } = 512;
        public string CustomJavaPath { get; set; } = string.Empty;
        public string CustomJvmArgs { get; set; } = string.Empty;
    }

    public class Library
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty; // Relative path
        public string Url { get; set; } = string.Empty;
        public string Checksum { get; set; } = string.Empty;
        public bool IsNative { get; set; } = false;
    }

    public class Argument
    {
        public string Value { get; set; } = string.Empty; // The actual argument string
        // Simple rule representation for now. Real logic is more complex.
        public bool IsAllowed { get; set; } = true; 
    }

    public class LogConfig
    {
        public string Argument { get; set; } = string.Empty;
        public LogFile File { get; set; } = new LogFile();
        public string Type { get; set; } = string.Empty;
    }

    public class LogFile
    {
        public string Id { get; set; } = string.Empty;
        public string Sha1 { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Url { get; set; } = string.Empty;
    }
}