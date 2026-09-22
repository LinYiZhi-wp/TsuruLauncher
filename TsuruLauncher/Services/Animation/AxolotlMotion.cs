using System;
using System.Windows;
using System.Windows.Media.Animation;

namespace TsuruLauncher.Services.Animation
{
    /// <summary>
    /// Axolotl（Mystic-Stars/Axolotl）动效令牌表 —— 逐条从源码抄来的原始数值，
    /// 不做任何「克制」化的削减。每条都标注了来源文件与行号，便于对照。
    ///
    /// 基础事实（决定了所有 Tailwind 工具类的实际数值）：
    ///   * apps/app-frontend/package.json → tailwindcss ^3.4.19
    ///   * packages/tooling-config/tailwind/tailwind-preset.ts 只扩展了 colors / backgroundImage，
    ///     没有 transitionDuration / transitionTimingFunction / keyframes / animation，
    ///     所以全部走 Tailwind v3 默认值：
    ///       transition-*             -> 150ms cubic-bezier(0.4, 0, 0.2, 1)
    ///       ease-out                 -> cubic-bezier(0, 0, 0.2, 1)
    ///       ease-in                  -> cubic-bezier(0.4, 0, 1, 1)
    ///       ease-in-out              -> cubic-bezier(0.4, 0, 0.2, 1)
    ///       duration-100/150/200/300 -> 100/150/200/300ms
    ///       duration-125             -> Tailwind v3 没有这一类，实际不生效，
    ///                                   落回 transition 自带的 150ms（见 TeleportOverflowMenu）
    ///   * packages/assets/styles/variables.scss -> --hover-brightness: 0.9(light) / 1.25(dark)
    /// </summary>
    public static class AxolotlMotion
    {
        #region 缓动曲线（CSS cubic-bezier 原值）

        /// <summary>CSS ease — global.scss:305/332/347（页面进出、.slide）。</summary>
        public static readonly IEasingFunction Ease = Freeze(new CubicBezierEase(0.25, 0.1, 0.25, 1.0));

        /// <summary>CSS ease-in-out / Tailwind 默认曲线 — 按钮颜色、.nav-rail-slider。</summary>
        public static readonly IEasingFunction EaseInOut = Freeze(new CubicBezierEase(0.4, 0.0, 0.2, 1.0));

        /// <summary>CSS ease-out / Tailwind ease-out — ButtonFrame.vue:18。</summary>
        public static readonly IEasingFunction EaseOut = Freeze(new CubicBezierEase(0.0, 0.0, 0.2, 1.0));

        /// <summary>CSS ease-in / Tailwind ease-in — 离场用。</summary>
        public static readonly IEasingFunction EaseIn = Freeze(new CubicBezierEase(0.4, 0.0, 1.0, 1.0));

        /// <summary>App.vue:3378 — 右栏宽度 --right-bar-width 320ms cubic-bezier(0.22, 1, 0.36, 1)。</summary>
        public static readonly IEasingFunction SidebarEase = Freeze(new CubicBezierEase(0.22, 1.0, 0.36, 1.0));

        /// <summary>
        /// 页面进场位移 / 缩放曲线 —— 任务 §1 指定的 <c>cubic-bezier(0.22, 1, 0.36, 1)</c>，
        /// 即已有的 <see cref="SidebarEase"/> 原值（不新造曲线，直接复用同一个 <see cref="CubicBezierEase"/>）。
        /// </summary>
        public static readonly IEasingFunction PageEnterMoveEase = SidebarEase;

        /// <summary>
        /// 资源页左侧栏 212 与 64 之间折叠的宽度动画曲线 —— 同样是 Axolotl 侧栏那一条
        /// <c>cubic-bezier(0.22, 1, 0.36, 1)</c>（直接复用同一个 <see cref="CubicBezierEase"/> 实例，
        /// 不新造曲线）。
        /// </summary>
        public static readonly IEasingFunction SidebarCollapseEase = SidebarEase;

        /// <summary>
        /// App.vue:3611 / FloatingActionBar.vue:269 —
        /// cubic-bezier(0.15, 1.4, 0.64, 0.96)，y1 = 1.4 是真的过冲（先冲过头再回落）。
        /// 导航按钮入场与右下悬浮胶囊入场都用它。
        /// </summary>
        public static readonly IEasingFunction OvershootEase = Freeze(new CubicBezierEase(0.15, 1.4, 0.64, 0.96));

        /// <summary>App.vue:3592 — 弹窗入场 cubic-bezier(0.51, 1.08, 0.35, 1.15)（轻微过冲）。</summary>
        public static readonly IEasingFunction PopupInEase = Freeze(new CubicBezierEase(0.51, 1.08, 0.35, 1.15));

        /// <summary>App.vue:3599 — 弹窗离场 cubic-bezier(0.68, -0.17, 0.23, 0.11)（先回抽再掉下去）。</summary>
        public static readonly IEasingFunction PopupOutEase = Freeze(new CubicBezierEase(0.68, -0.17, 0.23, 0.11));

        /// <summary>NavRail.vue:114 — 导航滑块淡入 250ms cubic-bezier(0.5, 0, 0.2, 1) 50ms。</summary>
        public static readonly IEasingFunction NavSliderFadeEase = Freeze(new CubicBezierEase(0.5, 0.0, 0.2, 1.0));

        /// <summary>UI 里最常见的 Tailwind 默认曲线，等价于 EaseInOut（语义别名）。</summary>
        public static readonly IEasingFunction TailwindDefault = EaseInOut;

        /// <summary>ModalLoadingIndicator.vue:23 / BaseTerminal.vue:255 2s cubic-bezier(0.4, 0, 0.6, 1) infinite。</summary>
        public static readonly IEasingFunction PulseEase = Freeze(new CubicBezierEase(0.4, 0.0, 0.6, 1.0));

        /// <summary>SymlinkMethodCards.vue:1386-1398 步骤翻页：出 150ms cubic-bezier(0.4,0,1,1)，入 170ms cubic-bezier(0,0,0.2,1)。</summary>
        public static readonly IEasingFunction StepOutEase = Freeze(new CubicBezierEase(0.4, 0.0, 1.0, 1.0));
        public static readonly IEasingFunction StepInEase = Freeze(new CubicBezierEase(0.0, 0.0, 0.2, 1.0));

        private static IEasingFunction Freeze(CubicBezierEase ease)
        {
            ease.Freeze();
            return ease;
        }

        #endregion

        #region 时长（毫秒）

        // ── 页面切换（global.scss:330-354 .page-slide-*）──
        /// <summary>进入：transition: opacity 0.18s ease。</summary>
        public const double PageEnterMs = 180;
        /// <summary>离开：transition: opacity 0.12s ease（比进入短，旧页面不会「顶」一下）。</summary>
        public const double PageLeaveMs = 120;

        // ── .slide（global.scss:302-317）位移 30px / 200ms ease ──
        /// <summary>transform 0.2s ease, opacity 0.2s ease。</summary>
        public const double SlideMs = 200;
        /// <summary>translateY(30px) / translateY(-30px)。</summary>
        public const double SlideOffsetPx = 30;

        // ── 页面进场「一眼可见」档（任务 §1）────────────────────────────────
        // 改前：opacity 180ms + translateY ±30px / 200ms ease（宿主只降到 0.92），
        //       实测肉眼几乎无感；改后幅度翻倍 + 加入 0.985 缩放 + 宿主降到 0.86。
        /// <summary>新页 opacity 0 -&gt; 1 时长 —— <b>180ms -&gt; 220ms</b>。</summary>
        public const double PageEnterFadeMs = 220;
        /// <summary>新页位移起点 —— <b>±30px -&gt; ±56px</b>。</summary>
        public const double PageEnterSlidePx = 56;
        /// <summary>新页位移时长 —— <b>200ms -&gt; 280ms</b>。</summary>
        public const double PageEnterSlideMs = 280;
        /// <summary>新页缩放起点 —— <b>无缩放 -&gt; scale(0.985)</b>。</summary>
        public const double PageEnterScaleFrom = 0.985;
        /// <summary>新页缩放时长（与位移同曲线同时长）。</summary>
        public const double PageEnterScaleMs = 280;

        // ── 列表 stagger（Settings.vue:395 / 766-791）──
        /// <summary>
        /// 错峰步长 —— <b>28ms -&gt; 40ms</b>（任务 §2：「一眼可见」档）。
        /// </summary>
        public const double StaggerStepMs = 40;
        /// <summary>错峰上限 —— <b>168ms -&gt; 200ms</b>（40ms × 5 项封顶）。</summary>
        public const double StaggerCapMs = 200;
        /// <summary>错峰单项时长 —— <b>180ms -&gt; 220ms</b>（原时长太短，逐项淡入看不出先后）。</summary>
        public const double StaggerItemMs = 220;
        /// <summary>错峰起点位移 —— <b>translateY(-6px) -&gt; -8px</b>（任务 §2）。</summary>
        public const double StaggerOffsetPx = -8;
        /// <summary>离场 opacity 100ms ease。</summary>
        public const double StaggerLeaveMs = 100;
        /// <summary>重排 transform 160ms ease。</summary>
        public const double StaggerMoveMs = 160;

        // ── 按钮（ButtonStyled.vue:267-271）──
        /// <summary>scale 0.125s ease-in-out。</summary>
        public const double ButtonScaleMs = 125;
        /// <summary>background-color/color/filter 0.25s ease-in-out。</summary>
        public const double ButtonColorMs = 250;

        // ── 按钮（ButtonFrame.vue:18-19）──
        /// <summary>duration-150 ease-out（0, 0, 0.2, 1）。</summary>
        public const double ButtonFrameMs = 150;
        /// <summary>enabled:active:scale-[0.97]。</summary>
        public const double ButtonFramePressScale = 0.97;

        // ── 按压缩放档位（按组件各自的 Tailwind 类）──
        /// <summary>ButtonStyled.vue:312 active:scale-95。</summary>
        public const double PressScale95 = 0.95;
        /// <summary>ButtonFrame.vue:19 / Tabs.vue:12 / FilterPills.vue:39 active:scale-[0.97]。</summary>
        public const double PressScale97 = 0.97;
        /// <summary>Instance.vue:272 / GridDisplay.vue:530 active:scale-[0.98]。</summary>
        public const double PressScale98 = 0.98;
        /// <summary>Settings.vue:683 :active { transform: scale(0.985) }。</summary>
        public const double PressScale985 = 0.985;

        /// <summary>small pill / tab 的 duration-100。</summary>
        public const double PillMs = 100;

        /// <summary>卡片 hover 颜色过渡 — HomeDashboard.vue:766-768 120ms ease。</summary>
        public const double WidgetHoverMs = 120;
        /// <summary>Settings.vue:695/747 图标与箭头 140ms ease。</summary>
        public const double IconColorMs = 140;
        /// <summary>Sidebar 折角手柄 180ms ease（App.vue:3516-3519, 3542）。</summary>
        public const double HandleMs = 180;
        /// <summary>折角手柄箭头 hover scale(1.12) / active scale(0.9)。</summary>
        public const double HandleHoverScale = 1.12;
        public const double HandlePressScale = 0.9;
        /// <summary>侧栏折叠箭头 transition-transform duration-300。</summary>
        public const double ChevronMs = 300;

        // ── 浮层 / 弹窗 / 下拉 ──
        /// <summary>TeleportOverflowMenu.vue:18-23 — 有效 150ms（duration-125 不是 Tailwind v3 类）。</summary>
        public const double OverflowMenuMs = 150;
        /// <summary>TeleportOverflowMenu scale-75 -> scale-100。</summary>
        public const double OverflowMenuFromScale = 0.75;
        /// <summary>
        /// FloatingPanel.vue:296-302 原为 0.125s ease-in-out —— 实测 125ms 太快、看不出「推进来」，
        /// 提到 220ms（任务 §5 的 200~250ms 区间）。
        /// </summary>
        public const double FloatingPanelMs = 220;
        /// <summary>起点缩放 —— 0.85 -&gt; 0.94（任务 §5 统一到 0.94~0.96 区间）。</summary>
        public const double FloatingPanelFromScale = 0.94;
        /// <summary>实验室工具详情面板起点 translateY(20px)（任务 §5 要求位移 &gt;= 12px）。</summary>
        public const double FloatingPanelOffsetPx = 20;
        /// <summary>Combobox.vue:101-104 transition-opacity duration-150。</summary>
        public const double DropdownFadeMs = 150;
        /// <summary>CollapsibleAdmonition.vue:176-190 300ms ease-in-out + translateY(-10px)。</summary>
        public const double CollapseMs = 300;
        public const double CollapseOffsetPx = 10;
        /// <summary>ScrollToTopButton.vue:69-76 0.24s ease + translateY(10px)。</summary>
        public const double ScrollToTopMs = 240;
        /// <summary>SelectedProjectsFloatingBar.vue:236-243 — opacity 160ms / transform 180ms + translateY(.5rem) scale(.98)。</summary>
        public const double PreviewOpacityMs = 160;
        public const double PreviewTransformMs = 180;
        public const double PreviewOffsetPx = 8;
        public const double PreviewFromScale = 0.98;
        /// <summary>HomeCalendar.vue:469-476 工具提示 100ms ease + translateY(0.25rem)。</summary>
        public const double TooltipMs = 100;
        public const double TooltipOffsetPx = 4;
        /// <summary>App.vue:3656 .fade-enter-active { transition: 0.25s ease-in-out }。</summary>
        public const double FadeMs = 250;

        // ── 内容 / 模式切换（极简主页 <-> 信息主页、资源页内容区、右栏内容）──
        // Axolotl 里没有单独的「内容切换」类，同类语义由下面两组拼出来：
        //   过冲曲线 250ms  <- FloatingActionBar.vue:265-283 / App.vue:3611
        //                      （cubic-bezier(0.15, 1.4, 0.64, 0.96) 是原版唯一带过冲的曲线）
        //   起点 scale 0.96 <- FloatingActionBar.vue:278 离场终点 scale(0.96)
        //   起点 translateY(8px)  <- SelectedProjectsFloatingBar.vue:236-243 的 translateY(.5rem) = 8px
        /// <summary>新内容进场：transform/opacity 0.25s cubic-bezier(0.15, 1.4, 0.64, 0.96)。</summary>
        public const double ContentSwitchMs = 250;
        /// <summary>新内容起点 scale(0.96 -&gt; 0.95)（任务 §5 的 0.94~0.96 区间取中）。</summary>
        public const double ContentSwitchFromScale = 0.95;
        /// <summary>新内容起点 translateY(8px -&gt; 16px)（任务 §5 要求位移 &gt;= 12px）。</summary>
        public const double ContentSwitchOffsetPx = 16;
        /// <summary>
        /// 旧内容淡出 150ms ease —— 取「120~150ms」区间上限；曲线用 CSS ease
        /// （global.scss:305/332/347 页面进出那一条，语义即「旧内容先让位」）。
        /// </summary>
        public const double ContentSwitchLeaveMs = 150;

        // ── 浮层 / 覆盖层遮罩（实验室工具详情、创建实例向导）──
        /// <summary>遮罩淡入 —— 150ms -&gt; 200ms（任务 §5：实验室 / 向导这一组统一到 200~250ms）。</summary>
        public const double OverlayMaskMs = 200;
        /// <summary>FloatingPanel.vue:296-302 —— 返回列表时反向缩回 scale(0.94) / 淡出，120 -&gt; 200ms。</summary>
        public const double FloatingPanelLeaveMs = 200;
        /// <summary>创建实例向导关闭：反向 150ms -&gt; 200ms ease。</summary>
        public const double WizardPanelLeaveMs = 200;
        /// <summary>创建实例向导面板起点 translateY(12px -&gt; 20px)（任务 §5 要求位移 &gt;= 12px）。</summary>
        public const double WizardPanelOffsetPx = 20;

        // ── 弹窗（NewModal / PopupInEase 那一组）──
        /// <summary>App.vue:3592 弹窗入场 transform .25s cubic-bezier(0.51, 1.08, 0.35, 1.15)。</summary>
        public const double ModalPanelMs = 250;
        /// <summary>弹窗面板起点 scale(0.96)。</summary>
        public const double ModalPanelFromScale = 0.96;
        /// <summary>弹窗面板起点 translateY(24px)（上浮入场）。</summary>
        public const double ModalPanelOffsetPx = 24;
        /// <summary>NewModal.vue:68-108 遮罩 / 滚动渐隐 200ms ease-out（出 ease-in）。</summary>
        public const double ModalMaskMs = 200;
        /// <summary>弹窗遮罩终态不透明度（与原 DownloadPage 的 0.72 保持一致）。</summary>
        public const double ModalMaskOpacity = 0.72;

        // ── FloatingActionBar（= 右下悬浮胶囊，FloatingActionBar.vue:260-283）──
        /// <summary>入场 transform/opacity 0.25s cubic-bezier(0.15, 1.4, 0.64, 0.96)。</summary>
        public const double FloatingBarEnterMs = 250;
        /// <summary>离场 transform/opacity 0.25s ease。</summary>
        public const double FloatingBarLeaveMs = 250;
        /// <summary>入场起点 scale(0.5) translateY(-10rem) -> -160px。</summary>
        public const double FloatingBarEnterScale = 0.5;
        public const double FloatingBarEnterOffsetPx = -160;
        /// <summary>离场终点 scale(0.96) translateY(-0.25rem) -> -4px。</summary>
        public const double FloatingBarLeaveScale = 0.96;
        public const double FloatingBarLeaveOffsetPx = -4;
        /// <summary>位置变化 bottom/top 0.25s ease-in-out。</summary>
        public const double FloatingBarSlotMs = 250;

        // ── 右栏（App.vue:3378）──
        /// <summary>--right-bar-width 320ms cubic-bezier(0.22, 1, 0.36, 1)。</summary>
        public const double SidebarWidthMs = 320;

        // ── 资源页左侧栏折叠 / 展开（212 与 64 之间）──
        /// <summary>
        /// 左侧栏宽度动画时长：Axolotl 侧栏原值 320ms 太慢，这里取同一条曲线
        /// <c>cubic-bezier(0.22, 1, 0.36, 1)</c>（<see cref="SidebarEase"/>）的 200ms 快档。
        /// </summary>
        public const double SidebarCollapseMs = 200;
        /// <summary>折叠 / 展开时文字的淡入淡出时长（Tailwind transition 默认 150ms）。</summary>
        public const double SidebarTextFadeMs = 150;
        /// <summary>
        /// 展开方向文字的淡入延迟：80ms —— 落在 200ms 宽度动画的后段，
        /// 文字不会一上来就被还在变宽的容器挤压变形。
        /// </summary>
        public const double SidebarTextFadeInDelayMs = 80;

        // ── 导航 ──
        /// <summary>NavRail.vue:110-114 滑块 top/bottom/left/width 150ms cubic-bezier(0.4,0,0.2,1)。</summary>
        public const double NavSliderMs = 150;
        /// <summary>NavRail.vue:37 STAGGER_DELAY = 120ms（下滑时 top 延迟，上滑时 bottom 延迟）。</summary>
        public const double NavSliderStaggerMs = 120;
        /// <summary>NavRail.vue:114 滑块 opacity 250ms cubic-bezier(0.5,0,0.2,1) 50ms。</summary>
        public const double NavSliderFadeMs = 250;
        public const double NavSliderFadeDelayMs = 50;
        /// <summary>NavButton.vue:11 transition-all = 150ms 默认曲线。</summary>
        public const double NavButtonMs = 150;
        /// <summary>App.vue:3611 导航按钮入场 all 0.5s cubic-bezier(0.15, 1.4, 0.64, 0.96)。</summary>
        public const double NavButtonEnterMs = 500;
        /// <summary>App.vue:3615 导航按钮离场 all 0.25s ease。</summary>
        public const double NavButtonLeaveMs = 250;
        /// <summary>App.vue:3645 入场起点 scale: 0.5; translate: -2rem 0 -> -32px。</summary>
        public const double NavButtonEnterScale = 0.5;
        public const double NavButtonEnterOffsetPx = -32;
        /// <summary>App.vue:3651 离场终点 scale: 0.75。</summary>
        public const double NavButtonLeaveScale = 0.75;
        /// <summary>App.vue:3628 animation: pop 0.5s ease-in forwards（品牌色涟漪）。</summary>
        public const double NavRippleMs = 500;
        /// <summary>App.vue:3632-3642 keyframes pop -> 0% scale .5 / 50% opacity .5 / 100% scale 1.5。</summary>
        public const double NavRippleFromScale = 0.5;
        public const double NavRippleToScale = 1.5;
        public const double NavRippleMidOpacity = 0.5;

        // ── 卡片 / 图标 hover 缩放（Axolotl 里真实存在的 hover scale）──
        /// <summary>HomeMinimal.vue:226 实例图标 transition-transform group-hover:scale-[1.03]。</summary>
        public const double HoverScale103 = 1.03;
        /// <summary>InstanceIconPickerModal.vue:39 hover:scale-105。</summary>
        public const double HoverScale105 = 1.05;
        /// <summary>Gallery.vue:195 transition-transform duration-200 group-hover:scale-[1.02]。</summary>
        public const double HoverScale102 = 1.02;
        /// <summary>ServerCard.vue:117 卡片角标 scale-75 opacity-0 -> group-hover:scale-100 opacity-100。</summary>
        public const double CardBadgeFromScale = 0.75;

        // ── 骨架屏 / 加载（LoadingIndicator.vue:57-129）──
        /// <summary>骨架 animation: pop 4s ease-in-out infinite（0.25 -> 0.5 -> 0.25）。</summary>
        public const double SkeletonMs = 4000;
        public const double SkeletonDimOpacity = 0.25;
        public const double SkeletonBrightOpacity = 0.5;
        /// <summary>shimmer 4s ease-in-out infinite，translateX(-80%) -> translateX(80%)。</summary>
        public const double ShimmerMs = 4000;
        public const double ShimmerFromRatio = -0.8;
        public const double ShimmerToRatio = 0.8;
        /// <summary>LoadingIndicator.vue:86-102 每行延迟 0s / 0.3s / 0.6s。</summary>
        public const double SkeletonStepDelayMs = 300;
        /// <summary>ModalLoadingIndicator.vue:23 / BaseTerminal.vue:255 2s cubic-bezier(0.4, 0, 0.6, 1) infinite。</summary>
        public const double PulseMs = 2000;
        /// <summary>ContentSelectionBar.vue:328 indeterminate 1.5s ease-in-out infinite。</summary>
        public const double IndeterminateMs = 1500;
        /// <summary>ProgressBar.vue:101 / Admonition.vue:189 1s linear infinite。</summary>
        public const double LinearWaitingMs = 1000;

        // ── 其它 ──
        /// <summary>Settings.vue:969 搜索命中高亮 0.9s ease-in-out 2。</summary>
        public const double HighlightMs = 900;
        public const int HighlightRepeat = 2;
        /// <summary>TranslatedProjectDescription.vue:147 translation-float-in 0.5s ease-out both，translateY(12px)。</summary>
        public const double FloatInMs = 500;
        public const double FloatInOffsetPx = 12;
        /// <summary>ModTranslationJobList.vue:107 脉冲环 1.6s ease-out infinite，扩散 0.5rem = 8px。</summary>
        public const double RingPulseMs = 1600;
        public const double RingPulseRadiusPx = 8;
        /// <summary>SymlinkMethodCards.vue:1386-1398 步骤翻页：出 150ms / 入 170ms。</summary>
        public const double StepOutMs = 150;
        public const double StepInMs = 170;
        /// <summary>SymlinkMethodCards.vue:1363 method-shake 0.45s ease-in-out。</summary>
        public const double ShakeMs = 450;

        #endregion

        #region 数值换算

        /// <summary>CSS rem -> WPF px（Axolotl 根字号 16px）。</summary>
        public static double Rem(double value) => value * 16.0;

        /// <summary>CSS translateX(+-100%) 之类按元素宽度换算成像素。</summary>
        public static double Ratio(double ratio, double extent) => ratio * extent;

        public static Duration Ms(double milliseconds) => new Duration(TimeSpan.FromMilliseconds(milliseconds));

        #endregion

        /// <summary>
        /// 动效偏好：
        ///   <c>TSURU_MOTION=full</c>  -> 强制全量播放（自动化实测用）
        ///   <c>TSURU_MOTION=off</c>   -> 强制关闭，等价于系统「减少动画」打开
        ///   <c>TSURU_MOTION=auto</c>  -> 完全跟随系统 <see cref="SystemParameters.ClientAreaAnimation"/>
        ///   未设置                    -> <b>默认全量播放</b>
        /// </summary>
        public static string MotionPreference
        {
            get
            {
                try { return Environment.GetEnvironmentVariable("TSURU_MOTION") ?? ""; }
                catch { return ""; }
            }
        }

        /// <summary>
        /// 系统「减少动画」开关。Axolotl 把所有过渡都包在
        /// <c>@media (prefers-reduced-motion: no-preference)</c> 里，语义就是
        /// 「没有明确声明减少动画 -> 全量播放」。
        ///
        /// WPF 里语义最接近的是 <see cref="SystemParameters.ClientAreaAnimation"/>，
        /// 但它在「关闭窗口内动画效果」或远程桌面 / 无人值守会话里<b>默认就是 False</b>
        /// （本机实测 ClientAreaAnimation=False、MenuAnimation=False），
        /// 之前直接拿它当默认值，结果整套界面全部退化成硬切 ——
        /// 这正是「过渡动画呢？」的根因。
        /// 所以现在：默认全量播放；只有显式 opt-out 才降级：
        ///   * 环境变量 <c>TSURU_MOTION=off</c>，或
        ///   * 环境变量 <c>TSURU_MOTION=auto</c> 且系统确实声明了减少动画。
        /// </summary>
        public static bool FullMotionEnabled
        {
            get
            {
                string pref = MotionPreference;
                if (string.Equals(pref, "full", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(pref, "off", StringComparison.OrdinalIgnoreCase)) return false;

                if (string.Equals(pref, "auto", StringComparison.OrdinalIgnoreCase))
                {
                    try { return SystemParameters.ClientAreaAnimation && SystemParameters.MenuAnimation; }
                    catch { return true; }
                }

                // 默认（未显式声明减少动画）= 全量播放
                return true;
            }
        }

        /// <summary>
        /// 逐帧「动画属性实际值」采样开关（默认关）。
        /// 打开后 <see cref="PageTransition.PlayPageEnter"/> 与宿主回落补偿会各挂 3 个一次性
        /// DispatcherTimer，把 Opacity / TranslateTransform.Y / ScaleTransform.ScaleX 的**当前动画值**
        /// 写进日志（<c>[MotionPerf] page-enter sample ...</c> / <c>host-dip sample ...</c>）。
        /// 这是排查「动画到底跑没跑」的唯一硬证据（像素截图分不清「没跑」和「跑了但渲染没跟上」），
        /// 但每帧挂定时器有成本，所以默认关闭，需要时 <c>set TSURU_MOTION_TRACE=1</c> 启动。
        /// </summary>
        public static bool TraceValues
        {
            get
            {
                try { return string.Equals(Environment.GetEnvironmentVariable("TSURU_MOTION_TRACE"), "1", StringComparison.Ordinal); }
                catch { return false; }
            }
        }

        /// <summary>给启动日志用的一句话说明。</summary>
        public static string DescribeMotionPreference()
        {
            string os;
            try
            {
                os = "os(ClientAreaAnimation=" + SystemParameters.ClientAreaAnimation +
                     ", MenuAnimation=" + SystemParameters.MenuAnimation + ")";
            }
            catch { os = "os(<unavailable>)"; }

            string pref = MotionPreference;
            string mode = string.IsNullOrEmpty(pref) ? "default(full)" : pref;
            return "reduce-motion=" + mode + " " + os + " => animations=" + FullMotionEnabled;
        }

}

/// <summary>
/// 旧调用点兼容层（VersionSettingsPage 等仍在引用）。
/// 现在每个成员都直接指向 Axolotl 的真实曲线 / 时长，不再有任何「克制」化的自造值。
/// </summary>
public static class TransitionConfig
{
    public static Duration DefaultDuration => AxolotlMotion.Ms(AxolotlMotion.FadeMs);
    public static Duration FastDuration => AxolotlMotion.Ms(AxolotlMotion.ButtonScaleMs);
    public static Duration SlowDuration => AxolotlMotion.Ms(AxolotlMotion.CollapseMs);

    /// <summary>Axolotl .page-slide 进入 180ms。</summary>
    public static Duration PageEnterDuration => AxolotlMotion.Ms(AxolotlMotion.PageEnterMs);
    /// <summary>Axolotl .slide 位移 200ms。</summary>
    public static Duration PageSlideDuration => AxolotlMotion.Ms(AxolotlMotion.SlideMs);
    /// <summary>Axolotl .page-slide 离开 120ms。</summary>
    public static Duration PageExitDuration => AxolotlMotion.Ms(AxolotlMotion.PageLeaveMs);
    /// <summary>Axolotl stagger 单项 180ms。</summary>
    public static Duration StaggerItemDuration => AxolotlMotion.Ms(AxolotlMotion.StaggerItemMs);

    public static double SlideDistance => AxolotlMotion.SlideOffsetPx;
    public static double SlideDistanceSubtle => AxolotlMotion.CollapseOffsetPx;
    public static double PageEnterSlideY => AxolotlMotion.SlideOffsetPx;
    public static double StaggerSlideY => AxolotlMotion.StaggerOffsetPx;

    public static IEasingFunction SmoothEase => AxolotlMotion.Ease;
    public static IEasingFunction DecelerateEase => AxolotlMotion.EaseOut;
    public static IEasingFunction AccelerateEase => AxolotlMotion.EaseIn;
    public static IEasingFunction PageEnterEase => AxolotlMotion.Ease;
    public static IEasingFunction PageExitEase => AxolotlMotion.Ease;
    public static IEasingFunction SpringEase => AxolotlMotion.OvershootEase;
    public static IEasingFunction SoftSpringEase => AxolotlMotion.EaseInOut;
    public static IEasingFunction GentleSpringEase => AxolotlMotion.EaseInOut;
    public static IEasingFunction JellyEase => AxolotlMotion.OvershootEase;
}
}
