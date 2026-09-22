namespace TsuruLauncher.Models
{
    /// <summary>
    /// 游戏版本信息
    /// </summary>
    public class GameVersion
    {
        public string Id { get; set; } = "";                    // 版本ID（文件夹名）
        public string DisplayName { get; set; } = "";           // 显示名称
        public string MinecraftVersion { get; set; } = "";      // MC版本号
        public VersionType Type { get; set; }                   // Release/Snapshot
        public ModLoader? Loader { get; set; }                  // Forge/Fabric/NeoForge/null
        public string LoaderVersion { get; set; } = "";         // 加载器版本
        public VersionCategory Category { get; set; }           // 分类
        public string Icon { get; set; } = "📦";                // 图标
        public string GamePath { get; set; } = "";              // 所属游戏目录
    }

    public enum VersionType
    {
        Release,
        Snapshot,
        OldAlpha,
        OldBeta
    }

    public enum ModLoader
    {
        Forge,
        Fabric,
        NeoForge,
        Quilt
    }

    public enum VersionCategory
    {
        Moddable,   // 可装Mod（已安装Forge/Fabric）
        Vanilla,    // 常规版本（原版或OptiFine）
        Broken      // 错误的版本
    }
}