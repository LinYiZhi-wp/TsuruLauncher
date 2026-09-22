namespace TsuruLauncher.Models
{
    /// <summary>One entry in the home page activity/news feed.</summary>
    public class NewsItem
    {
        public string Icon { get; set; } = "📢";
        public string Title { get; set; } = string.Empty;
        public string Detail { get; set; } = string.Empty;
        public string? Url { get; set; }
    }
}