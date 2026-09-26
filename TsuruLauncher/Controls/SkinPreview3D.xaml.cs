using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace TsuruLauncher.Controls
{
    /// <summary>
    /// Minecraft 皮肤的 3D 预览（Viewport3D 拼六个方块）。
    ///
    /// 皮肤贴图按官方 64×64（1.8+）布局取 UV；如果是旧的 64×32 布局，
    /// 左臂 / 左腿在贴图里不存在，会自动镜像右臂 / 右腿。
    ///
    /// 交互：按住拖动旋转（Y 轴自由转，X 轴夹在 ±60° 防止翻过去）。
    /// 换皮肤时有淡入 + 轻微上浮的过渡，不做硬切。
    /// </summary>
    public partial class SkinPreview3D : UserControl
    {
        // ── 方块尺寸（单位随便定，比例对就行）──
        private const double Head = 8;
        private const double BodyW = 8, BodyH = 12, BodyD = 4;
        private const double LimbW = 4, LimbH = 12, LimbD = 4;

        /// <summary>
        /// 第二层（overlay）相对本体每边外扩的量。
        /// Minecraft 1.8+ 的皮肤有第二层（帽子 / 外套 / 袖子 / 裤腿），
        /// 是比本体大 0.5 格的同样方块、用另一组 UV。
        /// **少了这一层，皮肤看起来就是平的、没细节**（卫衣、头发厚度全都没了）。
        /// </summary>
        private const double OverlayGrow = 0.5;
        private const double OverlayHalf = OverlayGrow / 2;

        /// <summary>
        /// true = 纤细模型（Slim / Alex），手臂只有 **3** 格宽；false = 经典（Classic / Steve）4 格宽。
        ///
        /// ⚠ 这个必须跟着账号的 skin variant 走 —— 纤细皮肤的手臂贴图是 3 格宽，
        ///   按 4 格去采样会串到旁边的像素，渲染出来手臂是花的。
        /// </summary>
        public static readonly DependencyProperty SlimProperty =
            DependencyProperty.Register(nameof(Slim), typeof(bool), typeof(SkinPreview3D),
                new PropertyMetadata(false, OnSkinChanged));

        public bool Slim
        {
            get => (bool)GetValue(SlimProperty);
            set => SetValue(SlimProperty, value);
        }

        /// <summary>皮肤贴图（BitmapImage）。null 时显示占位。</summary>
        public static readonly DependencyProperty SkinSourceProperty =
            DependencyProperty.Register(nameof(SkinSource), typeof(BitmapImage), typeof(SkinPreview3D),
                new PropertyMetadata(null, OnSkinChanged));

        public BitmapImage? SkinSource
        {
            get => (BitmapImage?)GetValue(SkinSourceProperty);
            set => SetValue(SkinSourceProperty, value);
        }

        // 默认视角：yaw 偏 15.75° —— 抄的是 Axolotl 源码 use-skin-preview-fit.ts 里
        // initialRotation 的默认值（Skins.vue 那边传 Math.PI/8 = 22.5）。
        // 试过 -32° / -14° / 22.5°，都不如源码这个值接近参考。
        private readonly AxisAngleRotation3D _yaw = new(new Vector3D(0, 1, 0), 15.75);
        private readonly AxisAngleRotation3D _pitch = new(new Vector3D(1, 0, 0), 0);

        // ── idle 动画用：每个关节一个旋转轴（绕 X 轴前后摆）──
        private readonly AxisAngleRotation3D _headRot = new(new Vector3D(1, 0, 0), 0);
        private readonly AxisAngleRotation3D _rightArmRot = new(new Vector3D(1, 0, 0), 0);
        private readonly AxisAngleRotation3D _leftArmRot = new(new Vector3D(1, 0, 0), 0);
        private readonly AxisAngleRotation3D _rightLegRot = new(new Vector3D(1, 0, 0), 0);
        private readonly AxisAngleRotation3D _leftLegRot = new(new Vector3D(1, 0, 0), 0);

        private System.Windows.Threading.DispatcherTimer? _idleTimer;
        private readonly System.Diagnostics.Stopwatch _idleClock = new();

        /// <summary>idle 摆动周期（秒）—— 参考里的 idle 是很慢的呼吸感动作，不是走路。</summary>
        private const double IdlePeriodSeconds = 2.6;

        private Point _dragOrigin;
        private double _yawOrigin;
        private bool _dragging;

        public SkinPreview3D()
        {
            InitializeComponent();
            BuildEmptyModel();
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            MouseLeave += (_, __) => _dragging = false;
            Unloaded += (_, __) => StopIdle();   // 页面切走时别让定时器继续跑
        }

        // ── 拖动旋转 ───────────────────────────────────────────────

        private void OnDown(object sender, MouseButtonEventArgs e)
        {
            _dragging = true;
            _dragOrigin = e.GetPosition(this);
            _yawOrigin = _yaw.Angle;
            CaptureMouse();
        }

        private void OnMove(object sender, MouseEventArgs e)
        {
            if (!_dragging) return;
            var p = e.GetPosition(this);
            ApplyDrag(p.X - _dragOrigin.X, p.Y - _dragOrigin.Y);
        }

        /// <summary>
        /// 拖拽的核心算法（抽出来是为了能自检）。
        ///
        /// ⚠ 只用 dx —— 只允许左右转，竖直位移**完全不参与**。
        ///   之前竖直方向也在改 _pitch，拖一下整个人就仰过去了，很难看。
        ///   （_pitch 保留但恒为 0，旋转链路不用改）
        /// </summary>
        internal void ApplyDrag(double dx, double dy)
        {
            _yaw.Angle = _yawOrigin + dx * 0.6;
        }

        /// <summary>自检用：当前 yaw / pitch（度）。</summary>
        internal (double Yaw, double Pitch) CurrentAngles => (_yaw.Angle, _pitch.Angle);

        /// <summary>自检用：把拖拽起点设到当前角度。</summary>
        internal void BeginDragForTest() => _yawOrigin = _yaw.Angle;

        private void OnUp(object sender, MouseButtonEventArgs e)
        {
            _dragging = false;
            ReleaseMouseCapture();
        }

        // ── 模型 ───────────────────────────────────────────────────

        private void OnSkinChangedStatic(BitmapImage? bmp)
        {
            if (bmp == null) { BuildEmptyModel(); ShowPlaceholder(true); return; }
            try
            {
                BuildSkinModel(bmp);
                ShowPlaceholder(false);
                PlayEnter();
            }
            catch
            {
                BuildEmptyModel();
                ShowPlaceholder(true);
            }
        }

        private static void OnSkinChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => (d as SkinPreview3D)?.OnSkinChangedStatic(e.NewValue as BitmapImage);

        private void ShowPlaceholder(bool show)
        {
            Placeholder.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 离屏渲染缩略图时置 true —— 不播入场动画、不启动 idle 定时器
        /// （否则每张卡片都会跑一个 30fps 的定时器）。
        /// </summary>
        internal bool SuppressMotion { get; set; }

        /// <summary>
        /// 把一个皮肤离屏渲染成静态 3D 缩略图，给皮肤卡片用。
        ///
        /// 做法就是临时造一个本控件、量好尺寸、`RenderTargetBitmap.Render()` 抓一张。
        /// 比另写一套建模逻辑省事，也不会跟预览区的效果走偏。
        /// </summary>
        public static BitmapSource? RenderThumbnail(BitmapSource skin, bool slim, int size, double yawDeg)
        {
            try
            {
                var ctl = new SkinPreview3D
                {
                    SuppressMotion = true,
                    Slim = slim,
                    SkinSource = skin as BitmapImage,
                    Width = size,
                    Height = size,
                };
                ctl._yaw.Angle = yawDeg;
                ctl._pitch.Angle = 0;

                ctl.Measure(new Size(size, size));
                ctl.Arrange(new Rect(0, 0, size, size));
                ctl.UpdateLayout();

                var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(ctl);
                rtb.Freeze();
                return rtb;
            }
            catch (Exception ex)
            {
                Utilities.Logger.LogError(ex, "RenderThumbnail");
                return null;
            }
        }

        /// <summary>换皮肤时的入场：整体淡入 + 轻微上浮。</summary>
        private void PlayEnter()
        {
            if (SuppressMotion) { Root.Opacity = 1; return; }

            if (!Services.Animation.PageTransition.AnimationsEnabled)
            {
                Root.BeginAnimation(OpacityProperty, null);
                Root.Opacity = 1;
                return;
            }

            // 用项目自己那套 PlayOpacity（从「当前透明度」补到目标值，内部有终值兜底），
            // 比裸 DoubleAnimation 更稳 —— 裸动画在静态场景 / 离屏渲染下可能停在中途。
            Root.BeginAnimation(OpacityProperty, null);
            if (Environment.GetEnvironmentVariable("TSURU_SKIN3D_NOFADE") == "1")
            {
                Root.Opacity = 1;   // 对照实验：不做透明度动画
            }
            else
            {
                Root.Opacity = 0;
                Services.Animation.PageTransition.PlayOpacity(Root, 1, 260,
                    Services.Animation.AxolotlMotion.EaseOut);
            }

            var tt = new TranslateTransform(0, 10);
            Root.RenderTransform = tt;
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(280))
            {
                EasingFunction = Services.Animation.AxolotlMotion.EaseOut
            });
        }

        private void BuildEmptyModel()
        {
            StopIdle();
            var group = new Model3DGroup();
            group.Children.Add(new AmbientLight(Colors.White));
            ModelHost.Content = group;
        }

        /// <summary>
        /// idle 动画 —— 对应 Axolotl 的 <c>baseAnimation: 'idle'</c>
        /// （它那边是模型自带的 glTF 骨骼动画 clip，WPF 加载不了，所以这里用程序化摆动等效实现）。
        ///
        /// 动作很轻：手臂前后 ±5°、腿反向 ±2.5°、头 ±1.2°，周期 2.6s ——
        /// 是「站着呼吸」的感觉，不是走路。
        /// 尊重系统的「减少动态效果」：关掉动画时直接不启动。
        /// </summary>
        private void StartIdle()
        {
            StopIdle();

            if (SuppressMotion) return;   // 离屏渲染缩略图：不要定时器
            if (!Services.Animation.PageTransition.AnimationsEnabled) return;

            _idleClock.Restart();
            _idleTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(33)   // ~30fps 够用，皮肤预览不需要 60
            };
            int idleLogTick = 0;
            _idleTimer.Tick += (_, __) =>
            {
                double t = _idleClock.Elapsed.TotalSeconds;
                if (++idleLogTick % 30 == 0 && Environment.GetEnvironmentVariable("TSURU_SKIN3D_DEBUG") == "1")
                    Utilities.Logger.LogInfo($"[SkinPreview3D] idle t={t:F2}s 右臂={_rightArmRot.Angle:F2}° 左臂={_leftArmRot.Angle:F2}° 头={_headRot.Angle:F2}°");
                if (++idleLogTick % 30 == 0 && Environment.GetEnvironmentVariable("TSURU_SKIN3D_DEBUG") == "1")
                    Utilities.Logger.LogInfo($"[SkinPreview3D] idle t={t:F2}s 右臂={_rightArmRot.Angle:F2}° 左臂={_leftArmRot.Angle:F2}° 头={_headRot.Angle:F2}°");
                double phase = t * (2 * Math.PI / IdlePeriodSeconds);
                double swing = Math.Sin(phase);

                _rightArmRot.Angle = swing * 5.0;
                _leftArmRot.Angle = -swing * 5.0;
                _rightLegRot.Angle = -swing * 2.5;
                _leftLegRot.Angle = swing * 2.5;
                _headRot.Angle = Math.Sin(phase * 0.5) * 1.2;   // 头慢半拍
            };
            _idleTimer.Start();
        }

        private void StopIdle()
        {
            _idleTimer?.Stop();
            _idleTimer = null;
        }

        /// <summary>贴图预放大倍数（最近邻）。见 <see cref="BuildSkinModel"/> 里的说明。</summary>
        private static int TextureUpscale =>
            int.TryParse(Environment.GetEnvironmentVariable("TSURU_SKIN3D_UPSCALE"), out var v) && v > 0 ? v : 16;

        /// <summary>
        /// 用最近邻把位图整数倍放大 —— 直接按像素复制，不做插值。
        ///
        /// 为什么不用更省事的两条路：
        ///   · <c>RenderOptions.SetBitmapScalingMode(brush, NearestNeighbor)</c> 只对 2D 绘制生效，
        ///     对 3D 材质采样无效；
        ///   · <c>TransformedBitmap</c> / <c>DrawingVisual+RenderTargetBitmap</c> 走的是 WIC / 合成器的
        ///     线性缩放（实测 RTB 那条路还会把内容搞丢，角色直接不见了）。
        /// 手写循环 64×64 → 512×512 也就 26 万像素，开销可以忽略。
        /// </summary>
        private static BitmapSource UpscaleNearest(BitmapSource src, int scale)
        {
            // ⚠ 统一成 32bpp **Pbgra32（预乘 alpha）**。
            //   WPF 3D 材质采样走的是预乘路径，给直通 alpha（Bgra32）会导致
            //   整个模型几乎全透明（我踩过：角色只剩几个小色块，几乎看不见）。
            var conv = new FormatConvertedBitmap(src, PixelFormats.Pbgra32, null, 0);
            int sw = conv.PixelWidth, sh = conv.PixelHeight;
            int dw = sw * scale, dh = sh * scale;

            var srcBuf = new byte[sw * sh * 4];
            conv.CopyPixels(srcBuf, sw * 4, 0);

            var dstBuf = new byte[dw * dh * 4];
            for (int y = 0; y < dh; y++)
            {
                int srcRow = (y / scale) * sw * 4;
                int dstRow = y * dw * 4;
                for (int x = 0; x < dw; x++)
                {
                    int s = srcRow + (x / scale) * 4;
                    int d = dstRow + x * 4;
                    dstBuf[d] = srcBuf[s];
                    dstBuf[d + 1] = srcBuf[s + 1];
                    dstBuf[d + 2] = srcBuf[s + 2];
                    dstBuf[d + 3] = srcBuf[s + 3];
                }
            }

            var bmp = BitmapSource.Create(dw, dh, 96, 96, PixelFormats.Pbgra32, null, dstBuf, dw * 4);
            bmp.Freeze();

            if (Environment.GetEnvironmentVariable("TSURU_SKIN3D_DEBUG") == "1")
            {
                // 采样几个点确认放大后的内容是对的（不是全透明）
                int cx = Math.Min(12 * scale, dw - 1), cy = Math.Min(12 * scale, dh - 1);
                int ci = (cy * dw + cx) * 4;
                int si = (Math.Min(12, sh - 1) * sw + Math.Min(12, sw - 1)) * 4;
                Utilities.Logger.LogInfo(
                    $"[SkinPreview3D] 放大源像素 (12,12) BGRA=({srcBuf[si]},{srcBuf[si + 1]},{srcBuf[si + 2]},{srcBuf[si + 3]})");
                Utilities.Logger.LogInfo(
                    $"[SkinPreview3D] 贴图放大 {sw}×{sh} → {dw}×{dh} Pbgra32，" +
                    $"中心像素 BGRA=({dstBuf[ci]},{dstBuf[ci + 1]},{dstBuf[ci + 2]},{dstBuf[ci + 3]})");
            }

            return bmp;
        }

        private void BuildSkinModel(BitmapImage skin)
        {
            // ⚠⚠ 先把贴图用「最近邻」预放大 8 倍（64 → 512）。
            //
            // 为什么必须这么做：WPF 3D 采样材质贴图**只有双线性插值**，没有最近邻选项
            // （RenderOptions.SetBitmapScalingMode 只对 2D 绘制生效，对 3D 材质无效）。
            // 64×64 的皮肤铺到屏幕上约 500px，等于放大 ~8 倍 —— 双线性会在
            // **一个纹素的宽度（≈8 屏幕像素）**上把颜色糊开，像素风的锐利边缘就没了，
            // 看起来整张脸都是糊的（用户反馈「这个很模糊啊」）。
            //
            // 预放大到 512×512 之后，屏幕上一个纹素只占约 1px，双线性的过渡带
            // 从 8px 缩到 1px —— 观感基本等同于最近邻。
            var tex = UpscaleNearest(skin, TextureUpscale);

            // ⚠⚠ UV 的分母必须用**原始**皮肤尺寸（64×64），不能用放大后的 512×512！
            //   下面的 UV 原点（8,8 / 20,20 / 44,20 …）都是按官方 64×64 贴图布局写的；
            //   如果拿放大后的尺寸当分母，UV 会被整体缩小 8 倍，采样到完全错误的区域
            //   （表现：模型扭曲、甚至因为采到透明像素而整个看不见）。
            //   放大只是为了提升采样锐度，UV 空间不变。
            double texW = skin.PixelWidth, texH = skin.PixelHeight;
            bool legacy = texH < 64;   // 64×32 旧布局：没有左臂 / 左腿

            // 光照照抄 Axolotl 源码（SkinPreviewRenderer.vue）：
            //     <TresAmbientLight :intensity="2" />
            //     <TresDirectionalLight :position="[-3, 4, -2]" :intensity="1.2" />
            //   → 环境光占 2/(2+1.2) = 62%，只有**一盏**方向光，整体偏亮、方向感很轻。
            //   我之前自己搞了环境 0x58 + 两盏方向光，明暗关系跟参考完全不是一回事。
            //
            //   方向光位置 (-3,4,-2) → 传播方向 normalize((0,0,0)-(-3,4,-2)) = (0.56,-0.74,0.37)，
            //   换成 WPF 的右手系（相机在 +z）把 z 取反：(0.56,-0.74,-0.37)。
            //
            //   实算（环境 120 + 方向 136）：
            //     正面 N·L≈0.80 → 120 + 109 = 229
            //     侧面 N·L≈0.30 → 120 +  41 = 161
            //     背面 N·L≈0    → 120
            //   正面/侧面 = 1.4 倍 —— 柔和的体积感，暗面不发黑。
            var group = new Model3DGroup();
            group.Children.Add(new AmbientLight(Color.FromRgb(0x78, 0x78, 0x78)));
            group.Children.Add(new DirectionalLight(Color.FromRgb(0x88, 0x88, 0x88), new Vector3D(0.56, -0.74, -0.37)));

            // 贴图刷子。
            // ⚠ 必须显式指定 Absolute + Viewport=(0,0,1,1)：
            //   ImageBrush 默认是 RelativeToBoundingBox，而 3D 里"包围盒"取的是**该网格自己的
            //   UV 范围**。每个面都是独立的 GeometryModel3D，于是整张贴图被硬塞进单个面里
            //   —— 渲染出来就是每个面上一个缩小版整图（一堆彩色小方块）。
            //   绝对坐标 + (0,0,1,1) 才能让 UV(0..1) 老老实实映射到整张贴图。
            var brush = new ImageBrush(tex)
            {
                Stretch = Stretch.Fill,
                ViewportUnits = BrushMappingMode.Absolute,
                Viewport = new Rect(0, 0, 1, 1),
                TileMode = TileMode.None,
            };
            RenderOptions.SetBitmapScalingMode(brush, BitmapScalingMode.NearestNeighbor);

            // 模型坐标：y 向上，脚底在 y=0。
            //
            // 每个部件单独一个 Model3DGroup，四肢各挂一个「绕肩膀 / 髋部」的旋转 ——
            // 这样才能做 idle 动画（Axolotl 源码里 baseAnimation: 'idle'，
            // 它的模型带骨骼动画；WPF 加载不了 glTF clip，所以这里用程序化摆动等效实现）。
            var parts = new Model3DGroup();

            // 头（绕脖子）
            var headGroup = new Model3DGroup
            {
                Transform = new RotateTransform3D(_headRot, 0, LimbH + BodyH, 0)
            };
            AddBox(headGroup, brush, texW, texH, Head, Head, Head,
                   -Head / 2, LimbH + BodyH, -Head / 2,
                   (8, 0), (16, 0), (0, 8), (16, 8), (8, 8), (24, 8));
            // 帽子层（第二层）
            AddBox(headGroup, brush, texW, texH, Head + OverlayGrow, Head + OverlayGrow, Head + OverlayGrow,
                   -Head / 2 - OverlayHalf, LimbH + BodyH - OverlayHalf, -Head / 2 - OverlayHalf,
                   (40, 0), (48, 0), (32, 8), (48, 8), (40, 8), (56, 8));
            parts.Children.Add(headGroup);

            // 身体（不动）
            var bodyGroup = new Model3DGroup();
            AddBox(bodyGroup, brush, texW, texH, BodyW, BodyH, BodyD,
                   -BodyW / 2, LimbH, -BodyD / 2,
                   (20, 16), (28, 16), (16, 20), (28, 20), (20, 20), (32, 20));
            // 外套层（第二层）
            AddBox(bodyGroup, brush, texW, texH, BodyW + OverlayGrow, BodyH + OverlayGrow, BodyD + OverlayGrow,
                   -BodyW / 2 - OverlayHalf, LimbH - OverlayHalf, -BodyD / 2 - OverlayHalf,
                   (20, 32), (28, 32), (16, 36), (28, 36), (20, 36), (32, 36));
            parts.Children.Add(bodyGroup);

            // 手臂宽度：纤细模型 3 格，经典 4 格（腿永远是 4 格，不受影响）
            double armW = Slim ? 3 : LimbW;

            // 右臂（贴图右臂 = 角色右手，在 -X 侧）—— 绕肩关节
            var rightArmGroup = new Model3DGroup
            {
                Transform = new RotateTransform3D(_rightArmRot, -BodyW / 2 - armW / 2, LimbH + BodyH, 0)
            };
            if (Slim)
            {
                AddBox(rightArmGroup, brush, texW, texH, armW, LimbH, LimbD,
                       -BodyW / 2 - armW, LimbH, -LimbD / 2,
                       (44, 16), (47, 16), (40, 20), (47, 20), (44, 20), (51, 20));
                // 袖子层
                AddBox(rightArmGroup, brush, texW, texH, armW + OverlayGrow, LimbH + OverlayGrow, LimbD + OverlayGrow,
                       -BodyW / 2 - armW - OverlayHalf, LimbH - OverlayHalf, -LimbD / 2 - OverlayHalf,
                       (44, 32), (47, 32), (40, 36), (47, 36), (44, 36), (51, 36));
            }
            else
            {
                AddBox(rightArmGroup, brush, texW, texH, armW, LimbH, LimbD,
                       -BodyW / 2 - armW, LimbH, -LimbD / 2,
                       (44, 16), (48, 16), (40, 20), (48, 20), (44, 20), (52, 20));
                AddBox(rightArmGroup, brush, texW, texH, armW + OverlayGrow, LimbH + OverlayGrow, LimbD + OverlayGrow,
                       -BodyW / 2 - armW - OverlayHalf, LimbH - OverlayHalf, -LimbD / 2 - OverlayHalf,
                       (44, 32), (48, 32), (40, 36), (48, 36), (44, 36), (52, 36));
            }
            parts.Children.Add(rightArmGroup);

            // 左臂 —— 绕肩关节
            var leftArmGroup = new Model3DGroup
            {
                Transform = new RotateTransform3D(_leftArmRot, BodyW / 2 + armW / 2, LimbH + BodyH, 0)
            };
            if (legacy)
            {
                AddBox(leftArmGroup, brush, texW, texH, armW, LimbH, LimbD,
                       BodyW / 2, LimbH, -LimbD / 2,
                       (44, 16), (48, 16), (40, 20), (48, 20), (44, 20), (52, 20), mirrorX: true);
            }
            else if (Slim)
            {
                AddBox(leftArmGroup, brush, texW, texH, armW, LimbH, LimbD,
                       BodyW / 2, LimbH, -LimbD / 2,
                       (36, 48), (39, 48), (32, 52), (39, 52), (36, 52), (43, 52));
                AddBox(leftArmGroup, brush, texW, texH, armW + OverlayGrow, LimbH + OverlayGrow, LimbD + OverlayGrow,
                       BodyW / 2 - OverlayHalf, LimbH - OverlayHalf, -LimbD / 2 - OverlayHalf,
                       (52, 48), (55, 48), (48, 52), (55, 52), (52, 52), (59, 52));
            }
            else
            {
                AddBox(leftArmGroup, brush, texW, texH, armW, LimbH, LimbD,
                       BodyW / 2, LimbH, -LimbD / 2,
                       (36, 48), (40, 48), (32, 52), (40, 52), (36, 52), (44, 52));
                AddBox(leftArmGroup, brush, texW, texH, armW + OverlayGrow, LimbH + OverlayGrow, LimbD + OverlayGrow,
                       BodyW / 2 - OverlayHalf, LimbH - OverlayHalf, -LimbD / 2 - OverlayHalf,
                       (52, 48), (56, 48), (48, 52), (56, 52), (52, 52), (60, 52));
            }
            parts.Children.Add(leftArmGroup);

            // 右腿 —— 绕髋关节
            var rightLegGroup = new Model3DGroup
            {
                Transform = new RotateTransform3D(_rightLegRot, -LimbW / 2, LimbH, 0)
            };
            AddBox(rightLegGroup, brush, texW, texH, LimbW, LimbH, LimbD,
                   -LimbW, 0, -LimbD / 2,
                   (4, 16), (8, 16), (0, 20), (8, 20), (4, 20), (12, 20));
            // 裤腿层
            AddBox(rightLegGroup, brush, texW, texH, LimbW + OverlayGrow, LimbH + OverlayGrow, LimbD + OverlayGrow,
                   -LimbW - OverlayHalf, -OverlayHalf, -LimbD / 2 - OverlayHalf,
                   (0, 32), (4, 32), (0, 36), (4, 36), (8, 36), (12, 36));
            parts.Children.Add(rightLegGroup);

            // 左腿 —— 绕髋关节
            var leftLegGroup = new Model3DGroup
            {
                Transform = new RotateTransform3D(_leftLegRot, LimbW / 2, LimbH, 0)
            };
            if (legacy)
            {
                AddBox(leftLegGroup, brush, texW, texH, LimbW, LimbH, LimbD,
                       0, 0, -LimbD / 2,
                       (4, 16), (8, 16), (0, 20), (8, 20), (4, 20), (12, 20), mirrorX: true);
            }
            else
            {
                AddBox(leftLegGroup, brush, texW, texH, LimbW, LimbH, LimbD,
                       0, 0, -LimbD / 2,
                       (20, 48), (24, 48), (16, 52), (24, 52), (20, 52), (28, 52));
                AddBox(leftLegGroup, brush, texW, texH, LimbW + OverlayGrow, LimbH + OverlayGrow, LimbD + OverlayGrow,
                       -OverlayHalf, -OverlayHalf, -LimbD / 2 - OverlayHalf,
                       (0, 48), (4, 48), (0, 52), (4, 52), (8, 52), (12, 52));
            }
            parts.Children.Add(leftLegGroup);

            group.Children.Add(parts);
            StartIdle();

            // 整机绕 Y 轴（yaw）+ 绕 X 轴（pitch）旋转，再下移让**模型中心**落在原点。
            // ⚠ 模型自身 y 范围是 0..(腿+身+头)=0..32，中心在 16，所以位移是 -16。
            // ⚠ Transform3DGroup 的 Children **按顺序依次应用**：先转、后移，
            //    否则「先平移再旋转」会让模型绕原点甩出去。
            double totalH = LimbH + BodyH + Head;   // 32
            var rotY = new RotateTransform3D(_yaw);
            var rotX = new RotateTransform3D(_pitch);
            var translate = new TranslateTransform3D(0, -totalH / 2.0, 0);
            var t = new Transform3DGroup();
            t.Children.Add(rotY);
            t.Children.Add(rotX);
            t.Children.Add(translate);

            var root = new Model3DGroup { Transform = t };
            root.Children.Add(group);
            ModelHost.Content = root;

            try
            {
                var b = root.Bounds;
                Utilities.Logger.LogInfo(
                    $"[SkinPreview3D] 模型已建：位置 ({b.X:F1},{b.Y:F1},{b.Z:F1}) 尺寸 {b.SizeX:F1}×{b.SizeY:F1}×{b.SizeZ:F1} " +
                    $"贴图 {texW:F0}×{texH:F0} legacy={legacy} 子模型={group.Children.Count}");
            }
            catch { }

            // 首帧之后再报一次视口尺寸 / 渲染层 —— 3D 不出来时这两个是关键线索
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    var cap = System.Windows.Media.RenderCapability.Tier >> 16;
                    Utilities.Logger.LogInfo(
                        $"[SkinPreview3D] 视口 {Viewport.ActualWidth:F0}×{Viewport.ActualHeight:F0} " +
                        $"Root {Root.ActualWidth:F0}×{Root.ActualHeight:F0} " +
                        $"RootOpacity={Root.Opacity:F2} RootVis={Root.Visibility} " +
                        $"ViewportVis={Viewport.Visibility} 有模型={ModelHost.Content != null} " +
                        $"RenderTier={cap} 相机=({Cam.Position.X:F0},{Cam.Position.Y:F0},{Cam.Position.Z:F0}) " +
                        $"FOV={Cam.FieldOfView:F0}");
                }
                catch { }
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>
        /// 加一个长方体。六个面各自从贴图上取一块 UV。
        /// 参数顺序：top / bottom / right / left / front / back 的 (u,v) 左上角。
        /// </summary>
        private static void AddBox(Model3DGroup group, Brush brush, double texW, double texH,
                                   double w, double h, double d,
                                   double x, double y, double z,
                                   (double U, double V) top, (double U, double V) bottom,
                                   (double U, double V) right, (double U, double V) left,
                                   (double U, double V) front, (double U, double V) back,
                                   bool mirrorX = false)
        {
            // 六个面的四角（顺时针，从外部看）
            // front (+Z) / back (-Z) / right (-X) / left (+X) / top (+Y) / bottom (-Y)
            double x0 = x, x1 = x + w, y0 = y, y1 = y + h, z0 = z, z1 = z + d;

            // front
            AddQuad(group, brush, texW, texH, front, w, h,
                    new Point3D(x0, y1, z1), new Point3D(x1, y1, z1), new Point3D(x1, y0, z1), new Point3D(x0, y0, z1), mirrorX);
            // back
            AddQuad(group, brush, texW, texH, back, w, h,
                    new Point3D(x1, y1, z0), new Point3D(x0, y1, z0), new Point3D(x0, y0, z0), new Point3D(x1, y0, z0), mirrorX);
            // right (-X)
            AddQuad(group, brush, texW, texH, right, d, h,
                    new Point3D(x0, y1, z0), new Point3D(x0, y1, z1), new Point3D(x0, y0, z1), new Point3D(x0, y0, z0), mirrorX);
            // left (+X)
            AddQuad(group, brush, texW, texH, left, d, h,
                    new Point3D(x1, y1, z1), new Point3D(x1, y1, z0), new Point3D(x1, y0, z0), new Point3D(x1, y0, z1), mirrorX);
            // top (+Y)
            AddQuad(group, brush, texW, texH, top, w, d,
                    new Point3D(x0, y1, z0), new Point3D(x1, y1, z0), new Point3D(x1, y1, z1), new Point3D(x0, y1, z1), mirrorX);
            // bottom (-Y)
            AddQuad(group, brush, texW, texH, bottom, w, d,
                    new Point3D(x0, y0, z1), new Point3D(x1, y0, z1), new Point3D(x1, y0, z0), new Point3D(x0, y0, z0), mirrorX);

            if (Environment.GetEnvironmentVariable("TSURU_SKIN3D_DEBUG") == "1")
                Utilities.Logger.LogInfo($"[SkinPreview3D] 方块 x[{x0:F0},{x1:F0}] y[{y0:F0},{y1:F0}] z[{z0:F0},{z1:F0}]");
        }

        /// <summary>
        /// 一个四边形（两个三角形）。UV 用贴图绝对像素坐标写进 TextureCoordinates，
        /// 配合 ImageBrush 的 Absolute Viewport 就能正确采样。
        /// </summary>
        private static void AddQuad(Model3DGroup group, Brush brush, double texW, double texH,
                                    (double U, double V) origin, double faceW, double faceH,
                                    Point3D p0, Point3D p1, Point3D p2, Point3D p3, bool mirrorX)
        {
            double u0 = origin.U, v0 = origin.V;
            double u1 = origin.U + faceW, v1 = origin.V + faceH;

            if (mirrorX) (u0, u1) = (u1, u0);

            var mesh = new MeshGeometry3D
            {
                Positions = new Point3DCollection { p0, p1, p2, p3 },
                TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 },
                TextureCoordinates = new PointCollection
                {
                    new Point(u0 / texW, v0 / texH),
                    new Point(u1 / texW, v0 / texH),
                    new Point(u1 / texW, v1 / texH),
                    new Point(u0 / texW, v1 / texH),
                },
            };

            group.Children.Add(new GeometryModel3D
            {
                Geometry = mesh,
                Material = new DiffuseMaterial(brush),
                BackMaterial = new DiffuseMaterial(brush),
            });
        }
    }
}
