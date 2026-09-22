using CommunityToolkit.Mvvm.ComponentModel;

namespace TsuruLauncher.Models
{
    /// <summary>首页小组件（Axolotl 式网格，尺寸档 1x1 / 2x1 / 1x2 / 2x2 / 3x1 / 3x2）。</summary>
    public partial class HomeWidget : ObservableObject
    {
        // ═══ 布局参数：**逐条对齐 Axolotl** ═══
        //   apps/app-frontend/src/components/home/home-dashboard.ts
        //     HOME_WIDGET_GRID_GAP        = 16
        //     HOME_WIDGET_GRID_ROW_HEIGHT = 160
        //     getHomeGridColumnCount(w)   = min(4, max(1, floor((w + 16) / (240 + 16))))
        //     getHomeWidgetSpan(size, n)  = { columns: min(sizeCols, n), rows: sizeRows }
        //     width  = columnWidth * span.columns + GAP * (span.columns - 1)
        //     height = ROW_HEIGHT  * span.rows    + GAP * (span.rows    - 1)
        // 之前我写死「两列 / Unit=502」，既不是 Axolotl 的做法，也没跟上容器宽度。

        public const int Gap = 16;
        public const int GridRowHeight = 160;   // Axolotl HOME_WIDGET_GRID_ROW_HEIGHT
        public const int MinColumnWidth = 240;  // Axolotl minimumColumnWidth
        public const int MaxColumns = 4;        // Axolotl Math.min(4, …)

        /// <summary>仪表盘内容区实际可用宽度，由 HomePage 在 SizeChanged 时写入。</summary>
        public static double ContainerWidth { get; set; } = 1020;

        /// <summary>当前列数（1~4），完全照搬 Axolotl 的公式。</summary>
        public static int ColumnCount
            => Math.Min(MaxColumns, Math.Max(1,
                   (int)Math.Floor((Math.Max(0, ContainerWidth) + Gap) / (double)(MinColumnWidth + Gap))));

        /// <summary>单列宽度。</summary>
        public static double ColumnWidth
            => Math.Max(0, (ContainerWidth - Gap * (ColumnCount - 1)) / ColumnCount);

        public string Kind { get; set; } = "greeting";

        /// <summary>free（自由摆放）模式下的列/行坐标 —— Axolotl HomeWidgetPosition。</summary>
        public int X { get; set; }
        public int Y { get; set; }

        [ObservableProperty]
        private string _sizeKey = "1x1";

        /// <summary>"2x1" → Units=2 / Rows=1。</summary>
        public int Units => SizeKey.Length >= 3 ? SizeKey[0] - '0' : 1;
        public int Rows => SizeKey.Length >= 3 ? SizeKey[2] - '0' : 1;

        /// <summary>紧凑模式（行高缩小，一屏能放下更多小件）。</summary>
        public static bool CompactMode { get; set; }

        public double RowHeight => CompactMode ? 122 : GridRowHeight;

        /// <summary>按列数裁剪后的实际跨度（Axolotl getHomeWidgetSpan）。</summary>
        public int SpanColumns => Math.Min(Units, Math.Max(1, ColumnCount));
        public int SpanRows => Rows;

        /// <summary>像素宽（Axolotl：columnWidth * span.columns + gap * (span.columns - 1)）。</summary>
        public double PixelWidth => ColumnWidth * SpanColumns + Gap * (SpanColumns - 1);

        /// <summary>像素高（Axolotl：rowHeight * span.rows + gap * (span.rows - 1)）。</summary>
        public double PixelHeight => RowHeight * SpanRows + Gap * (SpanRows - 1);

        public void RefreshSize()
        {
            OnPropertyChanged(nameof(RowHeight));
            OnPropertyChanged(nameof(PixelWidth));
            OnPropertyChanged(nameof(PixelHeight));
        }

        public string Title => Kind switch
        {
            "greeting" => "问候语",
            "instance" => "置顶实例",
            "worlds" => "最近世界",
            "java" => "运行环境",
            "stats" => "游戏统计",
            "account" => "当前账号",
            "news" => "动态与资讯",
            "shortcuts" => "快捷操作",
            _ => Kind
        };

        public string Glyph => Kind switch
        {
            "greeting" => "👋",
            "instance" => "🎮",
            "worlds" => "🌍",
            "java" => "☕",
            "stats" => "📊",
            "account" => "👤",
            "news" => "📰",
            "shortcuts" => "⚡",
            _ => "🧩"
        };

        public string SizeHint => SizeKey + "  ·  点我切换大小";

        partial void OnSizeKeyChanged(string value)
        {
            OnPropertyChanged(nameof(Units));
            OnPropertyChanged(nameof(Rows));
            OnPropertyChanged(nameof(PixelWidth));
            OnPropertyChanged(nameof(PixelHeight));
        }
    }
}
