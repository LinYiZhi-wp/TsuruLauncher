namespace TsuruLauncher.Models
{
    public class ExportOptions
    {
        public string ExportPath { get; set; } = string.Empty;
        public string ModpackName { get; set; } = string.Empty;
        public string ModpackVersion { get; set; } = string.Empty;
        
        // Export selection
        public bool IncludeGameCore { get; set; } = true;     // .minecraft/versions/{id}
        public bool IncludeGameSettings { get; set; } = true; // options.txt
        public bool IncludeSaves { get; set; } = false;       // saves/
        public bool IncludeMods { get; set; } = true;
        public bool IncludeResourcePacks { get; set; } = true;
        public bool IncludeShaderPacks { get; set; } = true;
    }
}