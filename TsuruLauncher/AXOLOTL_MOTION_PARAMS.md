# Axolotl 动效参数清单（源码实测）与 Tsuru Launcher 对应表

> 来源：`Mystic-Stars/Axolotl` @ main（本机通过 jsDelivr / GitHub contents API 拉取真实源码逐行核对，
> 共下载 1024 个 `apps/app-frontend/src` + `packages/ui/src` + `packages/assets/styles` 源文件做全量 grep）。
> 所有数值都是**原始值**，没有做任何「克制」化削减。

## 0. 决定一切的两个基础事实

| 项 | 值 | 出处 |
|---|---|---|
| Tailwind 版本 | `^3.4.19` | `apps/app-frontend/package.json` / `packages/ui/package.json` |
| preset 扩展内容 | **只扩展 colors / backgroundImage**，没有 transitionDuration / transitionTimingFunction / keyframes / animation | `packages/tooling-config/tailwind/tailwind-preset.ts` |

因此所有 Tailwind 工具类都走 v3 默认值：

| 类 | 实际值 |
|---|---|
| `transition-*` | 150ms `cubic-bezier(0.4, 0, 0.2, 1)` |
| `ease-out` | `cubic-bezier(0, 0, 0.2, 1)` |
| `ease-in` | `cubic-bezier(0.4, 0, 1, 1)` |
| `ease-in-out` | `cubic-bezier(0.4, 0, 0.2, 1)` |
| `duration-100 / 150 / 200 / 300` | 100 / 150 / 200 / 300 ms |
| `duration-125` | **Tailwind v3 没有这一类 → 不生成 → 落回 transition 的 150ms**（`TeleportOverflowMenu.vue:18` 写的就是它） |
| `--hover-brightness` | `0.9`（light）/ `1.25`（dark） — `packages/assets/styles/variables.scss` |

## 1. 页面切换

| 场景 | 原始值 | 出处 |
|---|---|---|
| 进入 | `transition: opacity 0.18s ease` + `will-change: opacity` | `global.scss:331-338` |
| 离开 | `transition: opacity 0.12s ease; pointer-events: none` | `global.scss:346-353` |
| 叠放方式 | `.page-transition-layer { grid-area: 1 / 1; isolation: isolate }` —— 新旧页同格交叠，进入不等待离开 | `global.scss:319-328` |
| 开关 | `<Transition name="page-slide" :css="themeStore.getFeatureFlag('page_transitions')" appear>` | `App.vue:2904` |
| 位移（通用 `.slide`） | `transform 0.2s ease, opacity 0.2s ease`；enter-from `translateY(30px)`；leave-to `translateY(-30px)` | `global.scss:302-317` |
| 减少动画 | 全部包在 `@media (prefers-reduced-motion: no-preference)` 里 → **默认全量播放** | `global.scss:330` |

## 2. 左导航栏

| 项 | 原始值 | 出处 |
|---|---|---|
| 按钮 | `w-12 h-12 rounded-full transition-all`（150ms 默认曲线）；hover `bg-button-bg` + `text-contrast`；选中 `bg-[--color-button-bg-selected]` + `text-[--color-button-text-selected]` | `NavButton.vue:11,82-87` |
| 选中态底色 | `.router-link-active { background-color: transparent }`（底色由滑块负责） | `NavRail.vue:97-102` |
| 滑动胶囊 | `top/bottom/left/width 150ms cubic-bezier(0.4, 0, 0.2, 1)`；`opacity 250ms cubic-bezier(0.5, 0, 0.2, 1) 50ms` | `NavRail.vue:108-115` |
| 拉伸错峰 | `STAGGER_DELAY = '120ms'`：下滑时延迟 `top`，上滑时延迟 `bottom` | `NavRail.vue:37,63-64` |
| 导航按钮入场 | `transition: all 0.5s cubic-bezier(0.15, 1.4, 0.64, 0.96)`；起点 `scale: 0.5; translate: -2rem 0; opacity: 0` | `App.vue:3610-3648` |
| 导航按钮离场 | `transition: all 0.25s ease`；终点 `scale: 0.75; opacity: 0` | `App.vue:3614-3653` |
| 涟漪 | `animation: pop 0.5s ease-in forwards`；keyframes `0% scale .5 / 50% opacity .5 / 100% scale 1.5`，色 `--color-brand-highlight` | `App.vue:3622-3642` |

## 3. 按钮

| 组件 | 原始值 | 出处 |
|---|---|---|
| `ButtonStyled` | `transition: scale 0.125s ease-in-out, background-color 0.25s ease-in-out, color 0.25s ease-in-out, filter 0.25s ease-in-out`；`active:scale-95`；hover `brightness(var(--hover-brightness))` | `ButtonStyled.vue:267-271,312` |
| `ButtonFrame` | `transition-[background-color,color,box-shadow,filter,opacity,transform] duration-150 ease-out`；`enabled:active:scale-[0.97]`；hover `brightness-[--hover-brightness]` | `ButtonFrame.vue:18-21` |
| `token-classes` 按钮 | `transition-[opacity,filter,transform] hover:brightness-[var(--hover-brightness)] active:scale-[0.98]` | `token-classes.ts:23,25` |
| 皮卡选择页按钮 | `transition-[filter,transform] duration-200 enabled:hover:brightness-[--hover-brightness] enabled:active:scale-95` | `Skins.vue:1086-1105` |
| 小胶囊 / Tab | `transition-all duration-100 active:scale-[0.97]` | `Tabs.vue:12`、`FilterPills.vue:39`、`ConsoleFilterPills.vue:6`、`ContentTypeFilter.vue:26` |
| 复制按钮 | `transition-[opacity,filter,transform,outline] duration-200 ease-in-out hover:brightness-[1.25] active:scale-95 active:brightness-[0.8]` | `CopyCode.vue:3` |

## 4. 卡片 / 列表项

| 场景 | 原始值 | 出处 |
|---|---|---|
| 实例卡 / 服务器卡 | `transition-[border-color,filter,transform] hover:border-surface-5 hover:brightness-110 active:scale-[0.98]` | `Instance.vue:272`、`ServerCard.vue:81,174` |
| 网格卡 | `transition-all hover:brightness-90 active:scale-[0.98]` | `GridDisplay.vue:530` |
| 行卡 | `transition-all duration-200 hover:border-brand hover:bg-brand-highlight active:scale-[0.98]` | `InstanceRowCard.vue:15` |
| 卡内角标 | `scale-75 opacity-0 transition-all group-hover:scale-100 group-hover:opacity-100` | `ServerCard.vue:117`、`Instance.vue:328,412` |
| 首页实例图标 | `size-20 transition-transform group-hover:scale-[1.03]` | `HomeMinimal.vue:226` |
| 图标选择器 | `transition-transform hover:scale-105` | `InstanceIconPickerModal.vue:39` |
| 画廊缩略图 | `transition-transform duration-200 group-hover:scale-[1.02]` | `Gallery.vue:195` |
| 小件卡片 | `border-color 120ms ease, box-shadow 120ms ease`；手柄 `background-color 100ms ease, color 100ms ease`；chip `width 120ms ease` | `HomeDashboard.vue:766,804,822,836,850` |
| 设置搜索行 | `:active { transform: scale(0.985) }`；图标/箭头 `140ms ease`，hover 箭头 `translateX(1px)` | `Settings.vue:683,695,747,760` |

## 5. 右栏（信息面板）宽度

```scss
/* App.vue:3209-3213 / 3336-3345 / 3376-3380 */
@property --right-bar-width { syntax: '<length>'; inherits: true; initial-value: 0px; }
.app-contents { --right-bar-width: 0px; grid-template-columns: 1fr var(--right-bar-width); }
.app-contents.sidebar-enabled { --right-bar-width: 300px; }
@media (prefers-reduced-motion: no-preference) {
  .app-contents { transition: --right-bar-width 320ms cubic-bezier(0.22, 1, 0.36, 1); }
}
```

折角手柄（`App.vue:3486-3544`）：`transition: background-color 180ms ease, border-color 180ms ease, color 180ms ease`；
`:hover svg { transform: scale(1.12) }`；`:active svg { transform: scale(0.9) }`；`svg { transition: transform 180ms ease }`；
折叠箭头 `transition-transform duration-300` + `rotate-180`。

## 6. 弹窗 / 浮层 / 下拉

| 组件 | 原始值 | 出处 |
|---|---|---|
| 右下悬浮胶囊（FloatingActionBar） | 入 `transform/opacity 0.25s cubic-bezier(0.15, 1.4, 0.64, 0.96)`，起点 `scale(0.5) translateY(-10rem)`；出 `0.25s ease`，终点 `scale(0.96) translateY(-0.25rem)`；位置 `bottom/top 0.25s ease-in-out` | `FloatingActionBar.vue:260-283` |
| 溢出菜单 | `transition duration-125 ease-out`（实际 150ms `(0,0,0.2,1)`）；`scale-75 opacity-0` ↔ `scale-100 opacity-100`；离场 `ease-in` | `TeleportOverflowMenu.vue:17-24` |
| FloatingPanel | `transform 0.125s ease-in-out, opacity 0.125s ease-in-out`；`scale(0.85); opacity: 0` | `FloatingPanel.vue:296-302` |
| 下拉（Combobox） | `transition-opacity duration-150` | `Combobox.vue:100-105` |
| 弹窗滚动渐隐 | `transition-all duration-200 ease-out` / 出 `ease-in`；`opacity-0 max-h-0` ↔ `opacity-100 max-h-6` | `NewModal.vue:68-108` |
| 可折叠提示 | `opacity 300ms ease-in-out, transform 300ms ease-in-out`；`translateY(-10px)` | `CollapsibleAdmonition.vue:174-190` |
| 回到顶部 | `opacity 0.24s ease, transform 0.24s ease`；`translateY(10px)` | `ScrollToTopButton.vue:67-76` |
| 内容页悬浮预览 | `opacity 160ms ease, transform 180ms ease`；`translateY(0.5rem) scale(0.98)` | `SelectedProjectsFloatingBar.vue:236-245` |
| 日历提示 | `opacity 100ms ease, transform 100ms ease`；`translateY(0.25rem)` | `HomeCalendar.vue:469-476` |
| 通知栈 | `all 0.3s ease-in-out`；`translateX(100%) scale(0.95)` | `NotificationStack.vue:44-50` |
| 弹窗通知 | `all 0.3s ease-in-out`；`translateX(100%) scale(0.8)` | `PopupNotificationPanel.vue:379-388` |
| 通知面板 | `all 0.25s ease-in-out`；入 `translateY(100%) scale(0.8)`；出 `translateX(±100%) scale(0.8)` | `NotificationPanel.vue:283-300` |
| 通用 fade | `.fade-enter-active { transition: 0.25s ease-in-out }` | `App.vue:3655-3661` |
| 调查弹窗 | 入 `transform .25s cubic-bezier(0.51, 1.08, 0.35, 1.15)`；出 `cubic-bezier(0.68, -0.17, 0.23, 0.11)`；`translateY(10rem) scale(0.8) scaleY(1.6)`，`transform-origin: top center` | `App.vue:3589-3607` |
| 向导翻页 | 出 `150ms cubic-bezier(0.4, 0, 1, 1) both`；入 `170ms cubic-bezier(0, 0, 0.2, 1) both`；`translateX(±100%)` | `SymlinkMethodCards.vue:1383-1450` |

## 7. 列表错峰（stagger）

```ts
/* Settings.vue:395 */
:style="{ '--search-stagger': `${Math.min(index * 28, 168)}ms` }"
```
```scss
/* Settings.vue:766-791 */
.settings-search-enter-active { transition: opacity 180ms ease var(--search-stagger, 0ms),
                                            transform 180ms ease var(--search-stagger, 0ms); }
.settings-search-enter-from   { opacity: 0; transform: translateY(-6px); }
.settings-search-leave-active { transition: opacity 100ms ease; }
.settings-search-move         { transition: transform 160ms ease; }
```
即 **步长 28ms、上限 168ms、单项 180ms ease、起点 translateY(-6px)**。

## 8. 骨架屏 / 加载态

| 项 | 原始值 | 出处 |
|---|---|---|
| 骨架脉冲 | `background var(--surface-2); opacity: .25; animation: pop 4s ease-in-out infinite`；keyframes `from{opacity:.25} 50%{opacity:.5; border-color:var(--color-button-bg)} to{opacity:.25}` | `LoadingIndicator.vue:57-119` |
| shimmer | `animation: shimmer 4s ease-in-out infinite`；`from{translateX(-80%)} 50%,to{translateX(80%)}`；渐变 `linear-gradient(-45deg, transparent 30%, rgba(196,217,237,.075) 50%, transparent 70%)` | `LoadingIndicator.vue:68-79,121-129` |
| 行延迟 | nth-child(2)=0s / (3)=0.3s / (4)=0.6s | `LoadingIndicator.vue:81-103` |
| Tailwind pulse | `2s cubic-bezier(0.4, 0, 0.6, 1) infinite` | `ModalLoadingIndicator.vue:23`、`BaseTerminal.vue:255` |
| 进度条等待 | `1s linear infinite` | `ProgressBar.vue:101`、`Admonition.vue:189` |
| 不确定进度 | `1.5s ease-in-out infinite` | `ContentSelectionBar.vue:328` |
| 搜索命中高亮 | `0.9s ease-in-out 2` | `Settings.vue:969` |
| 翻译浮动入场 | `translation-float-in 0.5s ease-out both`，`translateY(12px)` | `TranslatedProjectDescription.vue:50,147` |
| 任务脉冲环 | `list-pulse 1.6s ease-out infinite`，`0 0 0 0 brand-50%` → `0 0 0 .5rem transparent` | `ModTranslationJobList.vue:107-115` |

## 9. Tsuru Launcher 对应表

| Axolotl 原版 | WPF 落地 | 文件 |
|---|---|---|
| 页面 opacity 180ms/120ms ease + `.slide` translateY(±30px) 200ms | `PageTransition.PlayPageEnter / PlayPageLeave`；旧页在 `RootFrame.Navigating` 抓 `RenderTargetBitmap` 做离场层，与新页同格交叠 | `Services/Animation/PageTransition.cs`、`MainWindow.xaml(.cs)` |
| `.nav-rail-slider` 150ms + 120ms stagger + opacity 250ms 50ms | `NavSlider` Border + `ScaleY`(原点顶边) + `TranslateY` 两段合成；`MainWindow.UpdateNavSlider` | `MainWindow.xaml(.cs)` |
| NavButton `transition-all` 150ms + 选中透明 | `NavRailButton` 模板：HoverLayer 150ms `cubic-bezier(0.4,0,0.2,1)`，`IsChecked` 只换前景色 | `MainWindow.xaml` |
| nav-button-animated 0.5s `(0.15,1.4,0.64,0.96)`，scale .5 / translate -2rem / opacity 0 | `PageAnimation.NavItemIn` → `PageTransition.PlayNavItemIn` | `Services/Animation/*`、`MainWindow.xaml` |
| `--right-bar-width 320ms cubic-bezier(0.22,1,0.36,1)` | `PageTransition.AnimateSidebarWidth` + `GridLengthAnimation`，面板固定 260px 由 `ClipToBounds` 裁切 | `Services/Animation/PageTransition.cs`、`MainWindow.xaml(.cs)` |
| FloatingActionBar 入场 `scale(0.5) translateY(-10rem)` 0.25s 过冲 | `PageAnimation.FloatingBarIn` → `PageTransition.PlayFloatingBar` | `Services/Animation/*`、`MainWindow.xaml` |
| ButtonStyled scale 0.125s ease-in-out + active:scale-95 | `FloatPillButton` / `iOS26.CapsuleButtonBrand` / `iOS26.SecondaryButton` 模板 Storyboard | `MainWindow.xaml`、`Styles/iOS26.xaml` |
| ButtonFrame duration-150 ease-out + active:scale-[0.97] | `HeroButtonStyle` / `iOS26.JellyButton`；`iOS26.SegmentItem` 快照 0.97 @100ms | `Styles/iOS26.xaml` |
| 卡片 active:scale-[0.98]（150ms 默认曲线） | `MotionAssist.PressScale` 附加属性（只动 ScaleTransform） | `Services/Animation/MotionAssist.cs` |
| HomeMinimal `group-hover:scale-[1.03]` | `MotionAssist.HoverScale="1.03"` | `Views/HomePage.xaml` |
| stagger 28ms / cap 168ms / 180ms ease / -6px | `PageAnimation.StaggerChildren` → `PageTransition.PlayStaggeredIn` | `Services/Animation/*`、`Views/HomePage.xaml` |
| CSS `cubic-bezier(...)` 曲线 | `CubicBezierEase`（WebKit UnitBezier 求解）+ `AxolotlMotion` 冻结实例 | `Services/Animation/CubicBezierEase.cs`、`AxolotlMotion.cs` |
| 骨架 pop 4s / shimmer 4s / 行延迟 0.3s | `PageTransition.PlaySkeletonPulse` / `PlayShimmer` | `Services/Animation/PageTransition.cs` |
| 向导翻页 150ms / 170ms translateX(±100%) | `PageTransition.PlayStepTransition` | `Services/Animation/PageTransition.cs` |
| `prefers-reduced-motion: no-preference` = 默认播放 | `AxolotlMotion.FullMotionEnabled`（默认 true） | `Services/Animation/AxolotlMotion.cs` |

### 已知取舍（诚实记录）

1. **卡片背景色的「渐变过渡」**：WPF 的 `Style TargetType="Border"` 无法在 `Trigger` 里对冻结的主题画刷做颜色动画（`SolidColorBrush.Color` 动画要求画刷不冻结）。
   因此卡片的 hover 仍是换色（配色令牌与 Axolotl 一致），但**过渡是瞬时的**；位移/缩放（`active:scale-[0.98]`）是真动画。
   带 `HoverLayer`/`CheckLayer` 的按钮类控件则完整复刻了 250ms 的颜色过渡。
2. **导航滑块的 120ms 两段错峰**：Axolotl 是同时动画 `top` 与 `bottom` 两个属性；WPF 用
   `TranslateY`（承担 top，下滑时 `BeginTime = 120ms`）+ `ScaleY`（原点在顶边，承担 bottom 的「先拉伸」）合成，
   视觉等价但实现机制不同。
3. `duration-125` 在原版里其实不生效（Tailwind v3 无此类），WPF 侧按**实际生效的 150ms** 实现。
