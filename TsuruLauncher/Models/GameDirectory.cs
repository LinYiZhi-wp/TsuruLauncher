namespace TsuruLauncher.Models
{
    /// <summary>
    /// 游戏目录（.minecraft文件夹）
    /// </summary>
    public class GameDirectory
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsDefault { get; set; }
        public DirectorySource Source { get; set; }
    }

    public enum DirectorySource
    {
        Auto,       // 自动检测的（官方启动器等）
        Manual,     // 用户手动添加
        Current     // 当前默认目录
    }
}