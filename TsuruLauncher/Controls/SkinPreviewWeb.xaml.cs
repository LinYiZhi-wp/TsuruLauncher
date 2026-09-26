using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using TsuruLauncher.Utilities;

namespace TsuruLauncher.Controls
{
    /// <summary>
    /// 皮肤 3D 预览 —— **用网页（three.js / WebGL）渲染，嵌在 WebView2 里**。
    ///
    /// 为什么不用 WPF 的 Viewport3D：
    ///   · 贴图采样**只有双线性插值**，没有最近邻 —— 像素风皮肤一定是糊的
    ///     （我试过预放大 16 倍绕过，但那是打补丁）
    ///   · 透明层（皮肤第二层：帽子 / 外套 / 袖子）**没有逐像素深度排序**，
    ///     方块一大就会和本体抢深度
    ///   · 光照 / 相机参数全靠手算，很难跟参考对齐
    /// WebGL 里这三件事分别就是一行设置：NearestFilter / alphaTest / 标准光源。
    ///
    /// 网页资源在 <c>Assets/SkinPreview/</c>，通过 WebView2 的虚拟主机映射加载
    /// （直接 file:// 会踩 CORS，ES module 加载不了）。
    /// </summary>
    public partial class SkinPreviewWeb : UserControl
    {
        /// <summary>皮肤贴图（BitmapImage）。null 时显示占位。</summary>
        public static readonly DependencyProperty SkinSourceProperty =
            DependencyProperty.Register(nameof(SkinSource), typeof(BitmapImage), typeof(SkinPreviewWeb),
                new PropertyMetadata(null, OnSkinChanged));

        public BitmapImage? SkinSource
        {
            get => (BitmapImage?)GetValue(SkinSourceProperty);
            set => SetValue(SkinSourceProperty, value);
        }

        /// <summary>true = 纤细模型（Slim / Alex），手臂 3 格宽。</summary>
        public static readonly DependencyProperty SlimProperty =
            DependencyProperty.Register(nameof(Slim), typeof(bool), typeof(SkinPreviewWeb),
                new PropertyMetadata(false, OnSkinChanged));

        public bool Slim
        {
            get => (bool)GetValue(SlimProperty);
            set => SetValue(SlimProperty, value);
        }

        /// <summary>排查用的啰嗦日志开关。</summary>
        private static bool Verbose =>
            Environment.GetEnvironmentVariable("TSURU_SKIN3D_DEBUG") == "1";

        /// <summary>披风贴图。null = 没有披风。</summary>
        public static readonly DependencyProperty CapeSourceProperty =
            DependencyProperty.Register(nameof(CapeSource), typeof(BitmapImage), typeof(SkinPreviewWeb),
                new PropertyMetadata(null, OnCapeChanged));

        public BitmapImage? CapeSource
        {
            get => (BitmapImage?)GetValue(CapeSourceProperty);
            set => SetValue(CapeSourceProperty, value);
        }

        private static void OnCapeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SkinPreviewWeb ctl) ctl.RefreshCape();
        }

        private void RefreshCape()
        {
            if (!_ready) { _pendingCape = true; return; }

            try
            {
                var cape = CapeSource;
                if (cape == null)
                {
                    _ = Eval("window.skinPreview.setCape('')");
                    return;
                }
                string arg = JsonSerializer.Serialize(ToDataUrl(cape));
                _ = Eval($"window.skinPreview.setCape({arg})");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "SkinPreviewWeb 推披风");
            }
        }

        private bool _pendingCape;
        private bool _ready;                  // 网页加载完成
        private bool _pendingPush;            // 有皮肤要推、但网页还没就绪
        private BitmapImage? _pendingSkin;
        private bool _pendingSlim;

        public SkinPreviewWeb()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += (_, __) => { /* WebView2 自己管生命周期 */ };
        }

        // ── 初始化 ─────────────────────────────────────────────────

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_ready) return;
            try
            {
                Log("[SkinPreviewWeb] 开始初始化 WebView2");

                // ⚠ 必须允许软件 WebGL（SwiftShader）。
                //   在没有 GPU / GPU 被禁用的机器上，Chromium 的 GpuProcess 会反复崩溃
                //   （日志里刷 GpuProcessExited），WebGL 直接起不来、预览一片空白。
                //   --enable-unsafe-swiftshader 让它在没有 GPU 时退回软件渲染。
                var env = await CoreWebView2Environment.CreateAsync(null, null,
                    new CoreWebView2EnvironmentOptions
                    {
                        AdditionalBrowserArguments = "--no-sandbox --disable-gpu-sandbox --enable-unsafe-swiftshader --ignore-gpu-blocklist",
                    });
                await View.EnsureCoreWebView2Async(env);
                Log("[SkinPreviewWeb] EnsureCoreWebView2Async 完成");

                var core = View.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.IsZoomControlEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = false;

                // 用虚拟主机映射，避免 file:// 下的 CORS 问题（ES module 会被拦）
                string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "SkinPreview");
                Log($"[SkinPreviewWeb] 资源目录 = {dir}（存在={Directory.Exists(dir)}）");
                core.SetVirtualHostNameToFolderMapping(
                    "skin.local", dir, CoreWebView2HostResourceAccessKind.Allow);
                Log("[SkinPreviewWeb] 虚拟主机映射完成，开始导航");

                core.WebMessageReceived += (_, a) =>
                {
                    try { Log($"[SkinPreviewWeb] {a.TryGetWebMessageAsString()}"); } catch { }
                };

                core.NavigationCompleted += (_, args) =>
                {
                    Log($"[SkinPreviewWeb] 导航完成 成功={args.IsSuccess} 状态={args.WebErrorStatus}");
                    if (!args.IsSuccess) return;
                    _ready = true;

                    // ⚠ 导航完成后**总是按当前值重新推一次**，不要只看 _pendingPush ——
                    //   控件被重建（页面切换 / 账号切换）时 _pendingPush 可能没被置上，
                    //   结果预览区一直停在「还没有皮肤」占位。
                    if (SkinSource != null) PushSkin(SkinSource, Slim);
                    else Placeholder.Visibility = Visibility.Visible;

                    if (_pendingCape) { _pendingCape = false; RefreshCape(); }
                };

                View.Source = new Uri("https://skin.local/index.html");
                Log("[SkinPreviewWeb] 已设置 Source");

            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "SkinPreviewWeb 初始化");
                Placeholder.Visibility = Visibility.Visible;
            }
        }

        // ── 推皮肤 ─────────────────────────────────────────────────

        private static void OnSkinChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is SkinPreviewWeb ctl) ctl.Refresh();
        }

        private void Refresh()
        {
            var skin = SkinSource;
            bool slim = Slim;

            if (skin == null)
            {
                Placeholder.Visibility = Visibility.Visible;
                _ = Eval("window.skinPreview && window.skinPreview.setSkin('', false)");
                return;
            }

            if (!_ready)
            {
                _pendingPush = true;
                _pendingSkin = skin;
                _pendingSlim = slim;
                return;
            }

            PushSkin(skin, slim);
        }

        private void PushSkin(BitmapImage? skin, bool slim)
        {
            _pendingPush = false;
            if (skin == null || !_ready) return;

            try
            {
                string dataUrl = ToDataUrl(skin);
                Log($"[SkinPreviewWeb] 推送皮肤 slim={slim} 大小={skin.PixelWidth}×{skin.PixelHeight} base64={dataUrl.Length}");
                Placeholder.Visibility = Visibility.Collapsed;

                // 用 JSON 序列化，省得自己转义
                string arg = JsonSerializer.Serialize(dataUrl);
                _ = Eval($"window.skinPreview.setSkin({arg}, {(slim ? "true" : "false")})");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "SkinPreviewWeb 推皮肤");
            }
        }

        /// <summary>把位图编码成 data URL 给网页用。</summary>
        private static string ToDataUrl(BitmapSource bmp)
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(bmp));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
        }

        private static void Log(string msg)
        {
            if (Verbose) Logger.LogInfo(msg);
        }

        private async Task Eval(string script)
        {
            try
            {
                if (_ready && View.CoreWebView2 != null)
                    await View.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "SkinPreviewWeb 执行脚本");
            }
        }

        // ── 自检用 ─────────────────────────────────────────────────

        /// <summary>
        /// 把网页内容抓成 PNG。
        ///
        /// ⚠ 不能用 <c>RenderTargetBitmap</c> —— WebView2 是独立的合成层（子 HWND），
        ///   普通的 WPF 截图抓不到它，只会得到一片空白。必须走它自己的 CapturePreviewAsync。
        /// </summary>
        internal async Task<bool> CaptureToFileAsync(string path)
        {
            try
            {
                if (!_ready || View.CoreWebView2 == null) return false;
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                using var fs = System.IO.File.Create(path);
                await View.CoreWebView2.CapturePreviewAsync(
                    CoreWebView2CapturePreviewImageFormat.Png, fs);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "SkinPreviewWeb 截图");
                return false;
            }
        }

        /// <summary>自检用：网页是否已就绪。</summary>
        internal bool IsReady => _ready;

        /// <summary>自检用：设置模型偏转角（度）。</summary>
        internal Task SetYawAsync(double deg)
            => Eval($"window.skinPreview.setYaw({deg.ToString(System.Globalization.CultureInfo.InvariantCulture)})");

        /// <summary>
        /// 自检用：让网页自己导出当前画面。
        /// 比 CapturePreviewAsync 可靠 —— 后者抓的是合成器缓存帧，页面没重绘时给旧画面。
        /// </summary>
        internal async Task<bool> ExportPngAsync(string path)
        {
            try
            {
                if (!_ready || View.CoreWebView2 == null) return false;

                string raw = await View.CoreWebView2.ExecuteScriptAsync("window.skinPreview.capturePng()");
                // ExecuteScriptAsync 返回的是 JSON 字符串（带引号、转义）
                string dataUrl = System.Text.Json.JsonSerializer.Deserialize<string>(raw) ?? "";
                int comma = dataUrl.IndexOf(',');
                if (comma < 0) return false;

                byte[] bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                await System.IO.File.WriteAllBytesAsync(path, bytes);
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "SkinPreviewWeb 导出 PNG");
                return false;
            }
        }
    }
}
