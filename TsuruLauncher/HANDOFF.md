# Tsuru Launcher —— 项目交接说明（给接手模型）

> 一句话：这是一个 **Windows 上的 Minecraft 启动器**，WPF/.NET 8 写的，中文界面，UI 参照开源项目 **Axolotl（美西螈启动器，Rust+Tauri+Vue）** 的结构与动效重做，但代码/素材全部自研。

---

## 1. 项目位置与命名
- 工程根目录：`C:\Users\Linyizhi\.gemini\TsuruLauncher\`（旧名 GeminiLauncher，已整体改名：命名空间 `TsuruLauncher.*`、exe `TsuruLauncher.exe`、窗口标题 "Tsuru Launcher"）
- 用户启动方式：`C:\Users\Linyizhi\.gemini\run_launcher.bat`（内部 `cd /d %~dp0TsuruLauncher` + `dotnet run --project TsuruLauncher.csproj`，等于每次都会重新编译）
- 配置文件：`%APPDATA%\TsuruLauncher\config.json`（**不在 exe 目录**，早期放 exe 目录曾因删 bin 丢过配置）
- 运行日志：`bin\Debug\net8.0-windows\TsuruLauncher.log`
- 用户背景：中文用户，对 UI 细节要求很高，会贴 Axolotl 截图要求"照这个做"，也会指出卡顿/掉帧/对齐问题

## 2. 技术栈
- .NET 8 / `net8.0-windows` / WPF / C# 12
- CommunityToolkit.Mvvm 8.2.2（`[ObservableProperty]`、`[RelayCommand]`）
- WPF-UI 3.0.4（`ui:SymbolIcon`、`ui:TextBox`、`ui:ToggleSwitch` 等）
- Newtonsoft.Json、SharpZipLib、System.Management、Microsoft.Web.WebView2
- 主题：自研 `Services\ThemeService.cs`（Light / Dark / OLED 三种模式 × 强调色），所有画刷运行时注入 Application.Resources，**UI 里一律用 `{DynamicResource ...}`**

## 3. 构建 / 运行 / 自检
```powershell
cd C:\Users\Linyizhi\.gemini\TsuruLauncher
# 构建（若启动器正在运行，bin 里的 exe 会被占用 -> 用 -o 输出到别处；或先关掉启动器）
dotnet build TsuruLauncher.csproj -o C:\Users\Linyizhi\.gemini\out-x -v q --nologo
# 直接用某个页面启动 + 自检（--page 支持 home/resources/download/lab/settings）
$env:TSURU_SOFTWARE_RENDER="1"
.\out-x\TsuruLauncher.exe --page home --audit-ui
# 12 秒后杀进程，读 out-x\TsuruLauncher.log，确认 0 个 [ERROR] 且有 "[Audit] done checked=.. low=.."
```
内置调试开关（都在 `App.xaml.cs` / `MainWindow.xaml.cs` / `Services\Animation\*`）：
- `--page <tag>`：直接进指定页
- `--audit-ui`：遍历可视树做 WCAG 对比度自检，把低对比度文字写进日志（`[Audit]`）
- `--log-window`：打开实时启动日志窗
- `TSURU_SOFTWARE_RENDER=1`：强制软件渲染（截图/远程桌面用）
- `TSURU_MOTION_DBG=1`：动效残留普查日志（默认关）
- `TSURU_MOTION_TRACE=1`：动画采样日志（page-enter / host-dip sample）
- `TSURU_FRAME_DBG=1`：**逐帧间隔采样**（`MotionPerf.ProbeFrames`）。同步阻塞埋点量不到渲染开销，
  这个才能把「卡一下 / 不流畅」变成数字：收尾写一行
  `[MotionPerf] frames <label> n=.. active=.. idle=.. avg=..ms p95=..ms max=..ms over16.7=..`。
  注意两条：收口按**墙钟**（动画跑完后 WPF 不再合成、`CompositionTarget.Rendering` 会停发，
  按帧时间收口永远等不到）；间隔 >100ms 的样本算「空闲」单独计数、不参与统计
  （否则会算出 2900ms 的假尖峰）。**渲染线程失效时这一行不出现（或 active=0），这本身就是卡死判据。**
- `TSURU_CACHE_BIG=1`：打开"大元素也挂缓存"路径（**本机危险，见 §6**）
- `TSURU_MOTION_SINK=0`：关闭"缓存/缩放下沉到内层容器"

## 4. 目录结构（要点）
- `Services\`：ConfigService（配置单例）、ThemeService（主题）、LaunchService（启动游戏）、JavaService / JavaRuntimeService（Java 探测与自动下载）、YggdrasilAuthService + AuthlibInjectorService（外置登录）、AccountManager / AuthenticationService（账号）、LaunchLogHub（启动日志广播）、ModJarInspector、CrashAnalyzerService
- `Services\Ecosystem\`：ModrinthService、CurseForgeService、ContentSourceService（双内容源路由）、ModLoaderService（Fabric/Forge/OptiFine 安装）、ModpackService
- `Services\Network\`：DownloadService、DownloadManagerService（下载队列/安装链）、VersionManifestService
- `Services\Animation\`：**动效系统**，见 §6
- `ViewModels\`：MainViewModel（外壳/首页/账号/版本）、ResourcesViewModel、ResourceDetailViewModel、DownloadViewModel、LoaderSelectionViewModel
- `Views\`：HomePage、ResourcesPage、ResourceDetailPage、DownloadPage（实例库+创建向导）、LoaderSelectionPage、LabPage（实验室）、SettingsPage、VersionSettingsPage（版本设置）、VersionSelectorPage、DownloadManagerPage、LaunchLogWindow
- `Controls\`：iOS26Dialog、ModInstallPickerDialog、NotificationToast、SegmentedIndicator（分段胶囊滑动指示块）、MacWindowButtons、**ResourcesFilterPanel（资源筛选卡片，由外壳右信息面板在「资源」页承载）**
- `Converters\`：若干转换器（StringMatchToBool、StringsMatch、DownloadCount、StepDotBrush、InverseBool、ExpandIconConverter 等）
- `Styles\`：Colors.xaml（颜色 key）、**Resources.xaml（跨页共享样式：ToolButton / FilterChip / GhostPill / FilterSectionTitle / PanelSectionTitle / SegTab）**、iOS26.xaml（全局样式，**只追加不改语义**）、GlassScrollBar.xaml
- 设计参考文档：`AXOLOTL_SPEC.md`（从 Axolotl 截图+源码提取的页面结构规范）、`AXOLOTL_MOTION_PARAMS.md`（**逐条带出处行号的动效参数表**）

## 5. 已实现的功能（大致完整度）
- **外壳**：自绘顶栏（品牌/前进后退/页面标题/状态/通知铃铛/重新加载/运行中实例状态/窗口按钮）、64px 左导航栏（滑动胶囊、选中态）、**右侧可折叠信息面板**（当前游玩账号 + 账号管理面板：账号列表/类型标签/复制/删除/三种添加账户；游玩洞察；每日小挑战；Minecraft 新闻）、右下悬浮胶囊（列表/网格/自定义三态）
- **首页**：两态可切换并记忆 —— 极简主页（问候语 + 实例卡 + 可选实例列表 + 开始游戏）与信息主页（从最近的实例开始 / 最近的世界 / 日期·今天游玩 / 当前账号 / 新闻）
- **发现内容**：类别胶囊（整合包/模组/资源包/数据包/光影）、来源切换（Modrinth / CurseForge）、排序、查看数量、网格/列表切换、真分页、行卡片（缩略图/平台徽标/标签/下载量/更新时间/+安装）、右栏筛选面板（类别长列表/包含内容/运行环境/游戏版本）、空状态、滚轮按区域路由
- **实例库 + 创建实例向导**：分段胶囊筛选、实例行卡片（安装/开始游戏）、空状态；向导含图标选择/名称/**游戏目录三选一**/版本类型/加载器，创建后五步安装步骤条（准备→版本 JSON→运行库→资源→加载器）
- **实验室**：工具卡列表（渐变缩略图/分类标签/收藏/进入）+ 搜索 + 分类筛选；6 个工具（崩溃日志分析、JVM 参数生成、渐变文字、颜色转换、种子工具、文本编码），工具面板承载原有功能
- **版本设置**：概览/设置/Mod 管理/导出/维护 五个标签，Mod 管理含内容管理面板（模组/资源包/光影/世界/截图/日志）
- **账号**：微软登录、离线、外置登录（Yggdrasil / LittleSkin，自动下载并注入 authlib-injector）
- **其它**：Java 自动探测与下载（Adoptium）、下载队列与进度、崩溃日志分析、启动游戏（含 natives 解压、classpath、log4j 参数、authlib 注入）

## 6. 动效系统（**重点，踩过的坑都在这里**）
文件：`Services\Animation\{PageTransition.cs, MotionAssist.cs, AxolotlMotion.cs, CubicBezierEase.cs, MotionPerf.cs, MotionDiagnostics.cs, PageAnimation.cs}`、`Controls\SegmentedIndicator.cs`

**核心规则**
- 动画只动 `Opacity` / `RenderTransform`（Translate/Scale）/ 少量 Width（宽度动画要挂 BitmapCache 且结束摘掉）
- 一切动画结束必须**强制归位**（`SettleElement` / 播放轮次号 `_playTokens` / `SettleAfter` 兜底定时器），绝不允许停在中间态
- 时长/曲线一律取 `AXOLOTL_MOTION_PARAMS.md`（Axolotl 实测原值），不要自己发明
- 页面切换：新页 opacity + translateY(±56px→0) + 宿主 1→0.86→1；**不缩放整页**
- 分段胶囊：`SegmentedIndicator` 附加属性（容器加 `Host="True"` + `Indicator="{Binding ElementName=xxPill}"`，指示块放 Canvas 里，靠单个 TranslateX + 自身 Width 动画，150ms `cubic-bezier(0.4,0,0.2,1)`）

**血泪坑（务必不要重犯）**
1. `Storyboard.SetTarget(anim, transform)` 指向 `Transform`(Freezable) 时**动画完全不生效**（只有 opacity 会动）→ 必须对 Transform 直接 `BeginAnimation`。
   **【2026-09-20 补充】这条不只是"不生效"，它的表现形式是「动画结束时元素猛跳一下」。**
   元素一开始就被 `Reset` 摆在偏移位置（如 translateY=56），整个动画期间**一动不动**，
   直到 `FinalizeElement` 把终值写回 0 —— 于是收尾那一帧整个元素平移 56px。
   抓屏逐帧算像素差可以直接看到：收尾处出现一次**数值固定**的巨跳（两次独立运行 diff 完全相同）。
   **历史影响面**：`AddScale` / `AddTranslateY` / `AddTranslateX` 三个方法全走 Storyboard，
   所以 `PlayPopup`（弹窗 / 悬浮面板 / 实验室工具浮层）、`PlayNavItemIn/Out`（导航按钮入场）、
   `PlayFloatingBar` 的 scale 与位移**从来没动过**。已于本轮全部改成直接 `BeginAnimation`。
   判据：**动画"跑了"但看起来只是淡入淡出、收尾还跳一下 → 先查变换是不是走了 Storyboard。**
2. **给整页根挂 BitmapCache 会让渲染线程雪崩**（本机 150% DPI / D3D renderTier=2）：主页根 1,281,280px、创建向导面板 916,625px 都触发过 `UCEERR_RENDERTHREADFAILURE (0x88980406)`，表现为"帧间隔 1.2 秒 / 窗口停在最后一帧但消息泵还在跑（UIA 看 responding=True，人看是卡死）"。对策：`MotionAssist.MaxCacheDevicePixels = 1.2MP` 安全线；需要动效就**把动画目标下沉到内层容器**（`MotionHost` / `ResolveScaleHost`，实测内层 1,155,592px 挂缓存安全）。
3. 主题画刷是**共享且被冻结**的 `SolidColorBrush`，不能做 `ColorAnimation`（会串色/抛异常）→ 颜色过渡要用"双层元素交叉淡入"或非冻结副本。
4. 折叠面板用 `Margin.Top` 立即改 + `TranslateY` 延迟 = 抖动；正确做法：位移只由**单个 TranslateTransform** 承担，Height/Width 不参与动画。
5. 同时渲染的多个动画元素要限制错峰数量（当前前 8~10 项），否则点击帧同步阻塞超预算。
6. 渲染线程一旦失效，`Storyboard.Completed` 永不触发 → 所有播放入口都必须有 `SettleAfter` 兜底（历史上实验室覆盖层/创建向导都因此"假死"）。
7. **自定义 `AnimationTimeline` 的属性必须是 `DependencyProperty`**。`BeginAnimation` 应用动画时会克隆 timeline，
   而 `Freezable.CloneCore` **只复制依赖属性** —— 普通 CLR 属性在克隆体上退回字段默认值，`GetCurrentValue`
   于是恒返回同一个值（动画「不生效」但日志一切正常）。`GridLengthAnimation` 就踩过这个：收起瞬间归零、
   展开卡满时长再弹出。判据：**日志说动画跑了、画面却是硬切 → 先查 timeline 的属性是不是 DP**。
8. **「缓存 + 紧随其后的 `Visibility` 变更」会让渲染线程崩**（UCEERR_RENDERTHREADFAILURE）。
   实验室工具浮层就是：面积贴着 1.2MP 安全线的元素挂了 BitmapCache，退出动画结束时覆盖层紧接着
   `Visibility=Collapsed` → 点「返回」必崩、整窗卡死。判据与对策：
   * 凡是**动画结束后要切 Visibility** 的整页级元素，一律用
     `motion:MotionAssist.CacheDisabled="True"` 关掉缓存（新增附加属性）；
   * 只缓存「动画期间一直可见、面积 ≤1.2MP」的容器；
   * 崩溃时的日志特征是 `DispatcherUnhandledException(渲染线程失效，只报一次)`，
     并且 `TSURU_FRAME_DBG=1` 的 `frames` 行会出现 `active=0`。
9. **整页浮层不要「Collapsed → Visible 就立刻播动画」**。此刻 `ActualWidth/Height` 还是 0，
   既挂不上缓存、又要在动画帧里做冷布局。正确做法：先 `Visible + Opacity=0`，
   把播放推到 `DispatcherPriority.Loaded`（布局之后）。
10. **浮层/面板自己叠的那层「底色」会盖住滑动指示块**。指示块放在 `Canvas` 里、而按钮所在的
    `StackPanel` 排在 `Canvas` 之后 → 按钮的 `Background` 是画在指示块**上面**的。
    所以 hover 触发器**必须**用 `MultiTrigger` 排除 `IsChecked=True`，
    否则「选中态一被悬停就被 hover 底色盖掉」（表现为「绿色滑下来变白、鼠标一走又变绿」）。
    同一个坑在 `VersionSettingsPage.TabButtonStyle` 踩过。
11. **圆角裁切的 `RectangleGeometry` 不要用 `Rect="0,0,10000,h"` 这种「超宽矩形 + 半径」的写法**。
    几何右边界在可视区之外 → **只有左边两个角被倒角、右边是直边**，
    而且半径和容器 `CornerRadius` 往往对不上（历史上 36 vs 16）。要「只圆上面两角」就直接用
    `Border` + `CornerRadius="R,R,0,0"`，不要用 Clip。

## 7. 环境陷阱（非常重要）
- **自动化会话的 shell 缺环境变量 → `dotnet restore` 直接崩**：本会话 Bash 工具里
  `APPDATA` / `SystemRoot` / `windir` / `NUGET_PACKAGES` 全为空，`dotnet restore` 报
  `NuGet.targets(679,5): error : Value cannot be null. (Parameter 'path1')`（NuGet 找不到用户级
  `%APPDATA%\NuGet\NuGet.Config`）。**不是工程问题**（新建最小工程同样失败），**也不是沙箱问题**。
  绕过：把工程 `obj/` 下的 5 个 NuGet 产物（`project.assets.json`、`project.nuget.cache`、
  `*.nuget.g.props`、`*.nuget.g.targets`、`*.nuget.g.dgspec.json`）备份到工程树外，
  构建前 `rm -rf obj*` 再把它们拷回 `obj/`，然后加 `--no-restore` 构建。
  另：本会话 PowerShell 工具不可用（任何命令静默 exit 1），要跑 PS 脚本得用
  `_ps.mjs`（读 `env.json` 注入完整环境后 `spawn powershell.exe -File`）。
- **`obj*` 目录**：项目根下如果存在多个 `obj`/`objX` 中间目录，WPF 的 `*_wpftmp.csproj` 会把它们的 `*.g.cs` 一起 glob 进来 → 几百个 `CS0102/CS0111` 重复定义。**构建前先删干净**：`Get-ChildItem <proj> -Directory -Filter "obj*" | Remove-Item -Recurse -Force`；或加 `-p:DefaultItemExcludes=obj*/**`。绝不要把备份目录放进工程树（曾因 `_q2backup\MainWindow.xaml.cs` 报重复类型）。
- **exe 占用**：启动器运行时 `dotnet build` 会 `MSB3027/MSB3021` 失败 → 输出到别的目录，或先关掉。
- **多开实例**：多个 TsuruLauncher 实例会互相抢前台（自动化点击会点到别的窗口）；跑自动化时最好只留一个实例。
- **子代理/自动化会话**：`pwsh` 工具可能 `spawn ENOENT` 且 `process.env` 为空 → 要显式传 `cwd` 并注入 `USERPROFILE/APPDATA/LOCALAPPDATA/TEMP/NUGET_PACKAGES/SystemRoot`，`stdio` 用文件重定向避免 EPERM。
- **`read` 工具有截断风险**：超大文件分批读时可能提前截断，**写回前必须核对行数**（曾把 `ResourcesPage.xaml`、`VersionSettingsPage.xaml.cs` 写坏，后者用 `ilspycmd` 反编译历史 DLL 才救回）。
- 文件名/命名空间改名前项目叫 GeminiLauncher；工作目录 `C:\Users\Linyizhi\.gemini\GeminiLauncher` **已不存在**。

## 8. 设计红线与用户偏好
- **动效的帧预算按「实测刷新间隔」算，不是 16.7ms。** 本机显示器是 **180Hz**
  （`MotionPerf` 实测刷新间隔 ≈4.6~4.9ms，≈205~217Hz），拿 60Hz 的 16.7ms 当尺子
  会得出「一切正常」的错误结论。见 §9.2 第 8 条。
- 颜色**一律** `{DynamicResource ...}`，禁止硬编码 `#RRGGBB` / `Brushes.White`（浅色模式会白字白底）
- 卡片：圆角 12 + 1px `GlassBorderBrush` 描边 + **无阴影**；内容区统一内边距 **顶 20 / 左右 20 / 底 16**
- 主按钮为强调色实心胶囊，次按钮浅底/透明；小标签 8px 圆角浅底胶囊
- 动效要**一眼可见且流畅**（用户明确拒绝"克制"），但禁止掉帧：同步阻塞 < 3ms、帧间隔 ≤ 16.7ms
- 只**借鉴** Axolotl 的结构/交互/动效参数，**不复制**其代码、图片、字体、文案
- 界面中文；默认浅色主题

## 9. 当前状态与待办

已通过：最终整合构建 **0 错误**；`--audit-ui` 逐页 0 运行时异常（home 85 / resources 136 / download 114 / settings 175 个文本，低对比度 0；lab 98 个文本、剩 6 条是彩色 emoji 的误报）。

### 9.1 本轮（2026-09-20）已完成

**A. 右栏展开/收起动效 —— 根因是 `GridLengthAnimation` 的 From/To 不是依赖属性**
- `Animatable.BeginAnimation` 应用动画时会**克隆 timeline**，而 `Freezable.CloneCore` **只复制依赖属性**；
  `From` / `To` / `EasingFunction` 原本是普通 CLR 属性，克隆体上全部退回默认值 → `GetCurrentValue` 恒返回
  `GridLength(0)`。表现：**收起瞬间归零、展开原地卡满 320ms 再突然弹出**（日志里却正常写着 "over 320ms"）。
- 修法：三者改成 `DependencyProperty`（`Services/Animation/PageTransition.cs` 的 `GridLengthAnimation`）。
- 顺带给 `AnimateSidebarWidth` 补上 `SettleAfter` 兜底（原实现只靠 `anim.Completed`，违反 §6 第 6 条）。
- 实测：收起把手 x 从 194 平滑走到 404；点击帧同步阻塞 **21.84ms → 1.72ms**（预算 3ms）。

**B. 极简主页重做**（`Views/HomePage.xaml`）
- 删掉卡内那个「像卡住的弹窗」的版本 ListBox（选实例统一走圆形按钮 → `VersionSelectorPage`），
  删掉与右栏重复的三个统计小卡（游戏时长 / 已装 Mod / 最近世界）。
- 卡片撑到 720 宽：左 = 图标 + 版本名/副标题，右 = 开始游戏 / 版本设置 / 圆形选实例。
- 垂直居中改用 `MinHeight={Binding ViewportHeight}` 的 Grid 包裹（ScrollViewer 用无限高度测量子元素，
  直接写 `VerticalAlignment="Center"` 不生效）。
- 文本自检数 86 → 64。

**C. 信息主页（网格视图）重排**
- hero 横卡「从最近的实例开始」跨整宽 —— 修掉**标题被「选版本」按钮压住**的重叠 bug（原来两者同格）。
- 下面两行 × 两列，**同一行卡片靠 Stretch 拉平**，两列总高自然对齐（旧版左列 210 / 右列 490，右侧拖出大片空白）。
- 分组：左 = 今天 / 最近的世界；右 = 当前账号（Java / 版本数贴底）/ Minecraft 新闻。

**D. 右栏按页面切换内容（资源页放筛选）**
- 筛选面板从 `ResourcesPage` 搬到右信息面板：新控件 `Controls/ResourcesFilterPanel.xaml`，
  DataContext 由 `MainWindow.SyncInfoPanelPageContent()` 在导航时注入该页的 `ResourcesViewModel`。
- 搬出 `Page` 后 `{RelativeSource AncestorType=Page}` 会失配，模板内绑定改成
  `{RelativeSource AncestorType=UserControl}`（`ElementName` 在 `DataTemplate` 里不可靠）。
- 共享样式上移到新字典 **`Styles/Resources.xaml`**（`ToolButton` / `FilterChip` / `GhostPill` /
  `FilterSectionTitle` / `PanelSectionTitle`），`App.xaml` 里合并；`ExpandIconConverter` 一并提到 App 级。
- **内容区腾出 240px → 资源卡自然排成两列**（HANDOFF 旧待办 §9.2 顺带解决）。
- 整页浏览态（搜索/筛选会触发）原本把左导航栏和右信息面板一起藏掉，会让筛选面板「点一下就消失」；
  现在 `FrameContainer` 只吃 0-1 两列、`ApplyShellPanels` / `SyncInfoPanelEdge` 用
  `InfoPanelKeptInOverlay` 让**资源页的右栏在整页态下保留**。
- 清理：删掉 `FilterPanelToggle` 样式、`ResourcesViewModel.IsFilterPanelCollapsed` + `ToggleFilterPanel`、
  以及 §9.5 那两个零引用的 `iOS26.PanelEdgeHandle` / `iOS26.PanelFoldHandle`。

**E. 实验室工具面板「进入卡一下 / 点返回卡死」**
- **卡死是真的渲染线程崩**：点「返回」必现 `UCEERR_RENDERTHREADFAILURE (0x88980406)`
  （HANDOFF §6.2 的同一个坑）。触发条件 = `ToolDetailOverlay` / `ToolDetailPanel`
  这两个**面积贴着 1.2MP 安全线**的元素（根 1281280 / 面板 1155592 设备像素）挂了
  BitmapCache，而覆盖层在退出动画结束时紧接着要 `Visibility=Collapsed` ——
  **缓存 + 布局变更 = 渲染线程崩**。
  修法：这两处不挂缓存（`motion:MotionAssist.CacheDisabled="True"`，新增的附加属性），
  退出侧再显式 `DetachMotionCache` + `PlayPopup(useCache:false)` 双保险。
  修后 4 个工具 × 多次运行全部 0 崩溃。
- **「卡一下」是覆盖层没布局就播动画**：`Visibility` 刚从 Collapsed 变 Visible 时
  `ActualWidth/Height` 还是 0 → `CacheDuring` 因「没尺寸」放弃挂缓存
  （日志 `cache-skip Border#ToolDetailOverlay px=0`）→ 这 200ms 里每帧都要
  **冷布局整棵覆盖层 + 重新栅格化整页**。修法：先把覆盖层挂成 Visible + opacity 0，
  把播放推到 `DispatcherPriority.Loaded`（布局之后）再跑。
  实测超预算帧 **9 → 1~5**，点击帧同步阻塞 3.7~4.9ms → 2.5~3.3ms（回到预算内）。

**F. 版本设置的内容分类胶囊（模组 / 资源包 / 光影 / 世界 / 截图 / 日志）**
- 旧实现是 code-behind 里的普通 `Button`：**没有 CornerRadius**（在一堆圆角里杵着直角方块，
  就是「既有圆角又有直角」）、**没有任何过渡**（选中直接换色）、还硬编码了
  `Brushes.Transparent` 与 `Argb(40,128,128,128)`（违反「颜色一律 DynamicResource」红线）。
- 现在用新样式 `SegTab`（`Styles/Resources.xaml`，圆角 10 + 1px GlassBorderBrush +
  HoverLayer + 选中态 150ms 交叉淡入）+ `seg:SegmentedIndicator` 滑动指示块。
- **关键**：这一行必须**常驻复用**。指示块在「首次布局」是直接落位不播动画的
  （日志 `skipped reason=load`），旧代码每次 `ShowContentTab` 都重建整行 → 每次都是首次布局
  → 指示块只会瞬移。现在整行只建一次（`_contentPillRow` 字段），切换时只更新计数与选中态。
  实测日志从 `skipped reason=load` 变成
  `pill-indicator slide reason=check axis=XY delta=-188px target="光影 · 0"`。

**G. 版本设置页统一 UI（本轮追加）**
- **「左边圆角右边直角」的真身**：页面原来那条 60px 标题条的 `Grid.Clip` 写的是
  `RectangleGeometry Rect="0,0,10000,60" RadiusX/Y="36"` —— 几何右边界在 **x=10000**，
  根本没被裁到，所以只有**左边**两个角被倒角、右边是直边（而且 36 和容器 RadiusCard=16 也不匹配）。
- **「这页像从外壳里剥离出来的」**：页面根是 `Border(iOS26.ContentLayer + RadiusCard + 40px 黑色投影)`
  包着那条标题条 —— 外壳的 FrameContainer 本身已经是圆角面板，于是变成**卡片套卡片**，
  加上标题条就完全像一个独立应用。而且页面还自己挂了 `Page.Triggers` 的进场 Storyboard，
  和外层 `PlayPageEnter` 抢同一个 RenderTransform（且没有归位兜底）。
- 改法：页面根换成和 HomePage / ResourcesPage 一样的结构 —— 直接铺在内容区上、
  只保留 `Margin="20,20,20,16"`；页头改成「标题 24 + 副标题 12 三级灰」的普通排版（不再有底色条）；
  去掉投影（设计红线：卡片无阴影）；删掉页面自己的进场 Storyboard。
  注意：页内那条「← 返回」按钮一并去掉了，返回统一走外壳顶栏（`RootFrame.GoBack`）。
- **「绿色滑下来变成白色，鼠标一走又变绿」**：`TabButtonStyle` 的 hover 触发器只写了
  `IsMouseOver`，**没排除已选中**。指示块 `VersionTabPill` 在 `Canvas` 里、而 `StackPanel` 排在它后面，
  所以 RadioButton 自己的 Background 是**画在绿色指示块上面**的 —— 鼠标停在选中项上时
  `GlassMediumBrush` 直接把绿盖掉。改成 `MultiTrigger(IsMouseOver=True + IsChecked=False)`。
  （`SegTab` 一开始就是 MultiTrigger，所以内容胶囊没有这个问题。）

**H. `VersionSelectorPage` 同一套问题（本轮追加）**
- 和 `VersionSettingsPage` 是同一个模板抄出来的：`Rect="0,0,10000,60"` 的圆角 Clip（左边圆角右边直角）、
  `Border(ContentLayer + RadiusCard + 40px 投影)` 卡片套卡片、自己播 `Page.Triggers` 进场 Storyboard。
- 按同样方式统一（页根换成 `Margin="20,20,20,16"` 的普通 Grid、页头改成「标题 24」+ 右侧文件夹按钮、
  去投影、去自己的进场 Storyboard）。
- 顺带修掉页头文件夹按钮里硬编码的 `#20FFFFFF` / `#40FFFFFF`（违反「颜色一律 DynamicResource」），
  改成 `Surface4Brush` / `Surface5Brush`。
- **注意：`VersionSettingsPage` 和 `VersionSelectorPage` 是同一个模板的两个副本**，
  以后改其中一个记得同步另一个（或者干脆抽成共享样式）。

**I. 缓存下沉不再选中不可见的子树（`MotionAssist.ResolveMotionHost`）**
- 候选过滤加了一条 `fe.IsVisible`：把缓存挂到 Hidden/Collapsed 的子树上毫无意义（它根本不参与渲染），
  却会把真正可见的内容容器挤掉。这是把工具浮层收起态改成 `Hidden` 时暴露出来的：
  页面进场的缓存下沉错选中了浮层里的 `ToolDetailPanel`，真正可见的工具列表反而没缓存。
- 配套：`ToolDetailOverlay` 收起态从 `Collapsed` 改成 `Hidden`（保留布局、不渲染、不响应命中），
  这样「打开」时不再需要冷布局整棵覆盖层。

**J. 「点进入卡一下」的真凶：日志在 UI 线程上同步写盘（本轮追加）**
- 逐帧明细（`MotionPerf` 新增 `head(ms)=[...]` 与 `slow=[#帧=ms@累计ms]`）暴露出：
  **动画本体是流畅的**（前 200ms 的帧间隔是 `2 8 0 1 12 4 5 6 6 11 6 5 6 5`），
  慢帧全部落在 **265ms 之后**，也就是动画收尾、那批定时器集中触发的位置。
- 真凶是 `Utilities/Logger.Log`：它在**调用线程（UI 线程）**上 `File.AppendAllText` ——
  打开句柄 + 定位末尾 + 写入 + 关闭，一次 0.3~2ms，遇到杀软/磁盘压力能飙到 10~30ms。
  而一次「打开实验室工具」要写 7~8 行日志，落点正好散在点击帧与 240/260/320ms 那几个定时器上，
  与实测慢帧位置完全吻合。（HANDOFF 自己早就写过「每多一行日志就是一次 File.AppendAllText，
  这是同步阻塞预算里最贵的东西」，只是没人把它跟掉帧联系起来。）
- 修法：**日志改成异步**。`Log()` 只入队（`ConcurrentQueue`），一条后台线程（`BelowNormal`）
  持有常开的 `StreamWriter`（`AutoFlush = true`）落盘；`App.OnExit` 调 `Logger.Shutdown()` 收尾。
  `AutoFlush` 保证每行写完即 Flush 到操作系统，所以 `taskkill /F` 强杀后外部依然读得到日志
  （最坏只丢最后 ~250ms 内还没被后台线程取走的行）。
- 效果：**30ms 以上的尖峰消失**（max 32.9ms → 21~26ms）；峰值区间从 17~33ms 收窄到 21~26ms。

**K. 冷布局消除（本轮追加）**
- `ToolDetailOverlay` 收起态 `Collapsed` → `Hidden`；6 个工具面板（`PanelCrash`…`PanelText`）同样
  `Collapsed` → `Hidden`。`Hidden` 保留布局、不渲染、不响应命中，所以切工具时不再需要
  重新 measure/arrange 那棵子树。
- 配套（必须一起做）：`MotionAssist.ResolveMotionHost` 的候选过滤加了 `fe.IsVisible` ——
  否则覆盖层参与布局后，页面进场的缓存下沉会错选中浮层里的 `ToolDetailPanel`，
  真正可见的内容容器反而没缓存（实测过）。
- 实测效果在噪声内（见 §9.2 第 8 条），但它是**方向正确**的：消除了「打开工具」这一帧的冷布局。

**L. 实验室工具进入动画改为「照搬页面进场」（本轮追加，用户提议）**
- 用户的判断是对的：**页面切换是流畅的，只有实验室的进入是「另一套写法」**。逐条对齐后发现三处结构性差异：

  | | `PlayPageEnter`（流畅） | 实验室进入（卡） |
  |---|---|---|
  | 整页元素做 scale | **绝不**（注释明写「页根本身绝不 scale，实测 1,281,280 设备像素」） | 做了（面板 0.94→1，1,155,592 设备像素） |
  | 挂缓存时机 | 布局完成后**下一帧**（`AttachMotionCacheAfterLayout`） | 点击帧同步挂（栅格化与动画首帧叠加） |
  | 动画元素数 | 1 个 | 2 个（遮罩 + 面板各一套曲线/时长/位移） |

- 改法：`OpenTool` 直接取页面进场那一组常量 —— `PageEnterSlideMs`(280ms) +
  `PageEnterMoveEase`(cubic-bezier(0.22,1,0.36,1)) + `PageEnterSlidePx`(56px)，
  `fromScale: 1.0`（不 scale），面板不再单独播动画只落终态；退出侧同参数镜像。
- **实测（同一次运行内对照）：**
  ```
  实验室进入: avg=9.01~9.77ms  dropped=31~39
  页面进场:   avg=9.88ms       dropped=38
  ```
  **两者已经跑出同一组数字** —— 实验室的进入不再比页面切换差。
- 仍然不挂 BitmapCache：栅格化一张 1.15M 设备像素的位图实测要 90~98ms，
  而且动画结束还要再拆一次，两次栅格化比整个 280ms 动画都贵。

**M. 「实验室进入卡一下」的真正根因：变换动画根本没生效（本轮最终定位）**
- 前面几轮一直在测帧率，方向错了。**改成抓屏逐帧算像素差**之后真相立刻出来：
  ```
  t=1975 diff=598024     ← 点击，开始动
  ...（正常变化）...
  t=2158..2236 diff=0    ← 连续 9 帧画面完全不变（~80ms）
  t=2241 diff=2076396    ← 突然跳到终态（整段最大的一次变化）
  ```
  **不是掉帧，是「动画演到一半停住、然后跳一下」。**
- 根因就是 §6 第 1 条那个坑的活标本：`AddTranslateY` / `AddScale` / `AddTranslateX`
  全都用 `Storyboard.SetTarget(anim, transform)`，而 transform 是 Freezable → **完全不生效**。
  于是 `PlayPopup(ToolDetailOverlay, offsetY: 56)` 的位移从未发生，浮层整个动画期间停在
  **下移 56px** 的位置，收尾时 `FinalizeElement` 把 Y 写回 0 → **整个浮层向上猛跳 56px**。
  两次独立抓屏那一跳的 `diff` **数值完全相同（2076396）**——固定量位移的铁证。
- 修法：三个 `Add*` 改成直接对 Transform 调 `BeginAnimation`（HANDOFF §6.1 要求的写法）。
  完成时机不受影响：所有调用点给 fade / scale / translate 传的都是同一时长，
  Storyboard 里只剩 opacity 一条，`Completed` 仍在正确时刻触发；
  而三个 `Add*` 在 `|to-from| < 0.0001` 时提前 return，所以「Storyboard 为空」等价于
  「一条动画都没起」，调用方立刻 `Finish()` 依然正确。
- **修复后同一处的逐帧像素差**（末段平滑衰减，巨跳消失）：
  ```
  362352 → 189495 → 25135 → 8809 → 0 → 0
  ```
- **影响面（一并修好）**：`PlayPopup`（弹窗 / 悬浮面板 / 实验室工具浮层）、
  `PlayNavItemIn` / `PlayNavItemOut`（导航按钮入场 scale 0.5 + translateX -32）、`PlayFloatingBar`。
  `PlayMotion`（页面进场用的）不走这三个方法，所以页面进场一直是好的 ——
  **这也解释了「为什么只有实验室卡、页面切换不卡」。**
- 另外：`PlayPopup` 只有一个 `ease` 参数，若把 `PageEnterMoveEase`（强前倾，
  前 37% 时间走完 89% 位移）同时用在**透明度**上，观感会是「猛动一下然后停住」。
  页面进场只把它用在位移上、透明度用平滑的 `Ease`。实验室这边已统一改用 `Ease`。

**N. 资源页（修对了 2 个、修不动 1 个）**
- 分类切换不显示资源：根因是「精选」这条链路（`LoadFeaturedContentAsync`）走完后
  **没有任何东西**把 `ResultsStack` 淡回来——之前的代码只监听 `HasSearchResults` / `IsBusy`，
  这两个在精选链路上都不会变。修：页面 `OnVmPropertyChanged` 加 `IsFeaturedLoading` case，
  `!IsFeaturedLoading` 时 `ScheduleResultsRefresh("featured-loaded")`。
- 资源缩略图全空白：根因是 `ModProject` **没有 `INotifyPropertyChanged`**，
  `IconImage` 是普通 auto-property。`PreloadImagesAsync` 是在卡片**已经加入集合之后**才
  `mod.IconImage = img`——绑定收不到通知，网格视图永远是空白。列表视图能显示
  是因为 `SearchRows` 有 `NotifyIconChanged()`。修：`ModProject` 实现 `INotifyPropertyChanged`，
  `IconImage` 用完整 setter 通知 `OnPropertyChanged()`。
  （`*webp` 不需要额外处理：WPF 在装了「WebP Image Extensions」或 Windows 11 自带 WIC 时可直接解码。）

**O. 删除顶栏「重新加载以更新」按钮**（`MainWindow.xaml` + `MainWindow.xaml.cs`）

**P. 重写 `VersionSelectorPage` 的版本行**
- 原版要求**双击**整行才选中，UI 也只露一个 ⚙️，用户不知道。
- 新版（`CreateVersionItem`）：
  - **单击整行** = 选中并确认（`MouseLeftButtonUp` 走和「选择」按钮同一条路）
  - **显式「选择」按钮**（accent 绿底白字，`MinWidth=76`）—— 让用户知道点哪里
  - ⚙ 设置按钮（透明底）
  - 单击行不会被 ⚙ /「选择」冒泡触发（`IsChildInteractive` 沿 VisualTreeHelper 向上检查是否撞到 ButtonBase）
  - 悬停浅抬亮（`AccentAlpha(35)`），选中（`HighlightSelectedVersion` 记录到 `_highlightedBorder`）走 `AccentAlpha(45)` 强调
  - 整张卡：`Surface4` 底 + `AccentAlpha(20)` 边 + 圆角 10 + 16/14 内边距
  - 取消双击（避免歧义）
- 用户原话「需要单击就能选择」/「做个按钮」/「ui也很丑」—— 三个诉求一次性处理。


N. 资源详情页（`ResourceDetailPage`）仍用旧英文长段式排版，用户反馈很丑——**未重做**。
   建议改：左侧描述区用 Markdown / 富文本 + 折叠的多段落；右上信息卡紧凑化；底部固定「安装 / 加入 / 打开网页」行动条。

O. 【已解决】右下「视图切换」胶囊 —— 真实情况与用户描述不同：
   * **可见性没问题**。用 WPF 侧运行时日志拿到确定性证据：
     `[PillDiag] null=False vis=Visible isVisible=True opacity=1 size=114.7x42.7 parent=Grid zIndex=900 col=1 loaded=True`，
     5 个页面**完全一致** → 胶囊在所有页面都渲染出来了。
     （**教训**：本构建的 UIA **整棵树都查不到非空 `AutomationId`**，连写死的 `S2NavIcon-mod` 也没有
      —— UIA 这条路不可用；而截图我又看不到。**唯一可行的是在应用内部打状态日志**。）
   * **真正的问题是「切换视图」只在首页生效**：`HomeListMode_Click` 无条件写 `vm.IsMinimalHome`，
     在资源页点它动的是首页的视图模式。用户说的「只在首页出现」实际是这个语义。
   * 修法：`ApplyPillViewMode(list)` 按当前页分派 —— 首页写 `IsMinimalHome`、
     资源页写 `ResourcesViewModel.IsListView`、其他页提示 + 回首页；
     并新增 `UpdatePillForCurrentPage()` 刷新选中态与「编辑」按钮可见性。
     ⚠ 它必须在 **`RootFrame_Navigated`** 里调，**不能**放在 `SyncNavRailSelection` 后面 ——
     后者在导航完成**前**执行，那时 `RootFrame.Content` 还是旧页，拿到的是上一页的状态（实测踩过）。

1. 创建实例向导里「**安装步骤条**」那块曾误删 16 行、按 BAML 重建，需人工点一次创建实例目视复核（仅安装阶段可见）
2. 实验室第 6 张卡片在默认窗口下需滚动才能点到（可接受，或改紧凑两列卡）
3. 实验室列表在首次进入/返回后 **UIA 自动化树丢失卡片 peer**（只影响自动化，不影响手点；
   本轮做循环测试时复现过：点完「返回」后 UIA 里找不到「进入」按钮）
4. 卡片 hover 的**背景色**仍是瞬时换色（冻结画刷限制），要真过渡得把卡片改成 `ContentControl` + 模板
5. 深色/OLED 主题下的整体观感未系统性复查（重点一直放在浅色）
6. 验收时误装进 `%APPDATA%\.minecraft\versions\26.3` 的测试实例（可删）
7. `VersionSettingsPage.xaml.cs` 的 `ShowContentTab` 仍是「清空 ContentArea 再重建」，
   只有胶囊行做了常驻；内容本体每次重建（可接受，但若要做内容进场动画需再拆一层）
8. **【最重要】本机显示器是 ~200Hz，不是 60Hz —— 项目里「帧间隔 ≤ 16.7ms」这条预算是错的。**
   实测（`Get-CimInstance Win32_VideoController` + `MotionPerf` 自动标定）：
   ```
   gpu NVIDIA GeForce RTX 5060 Laptop  cur=2560x1600  refresh=180Hz
   MotionPerf 实测刷新间隔 ≈ 4.6~4.9ms（≈205~217Hz）
   ```
   **真正的预算是 5ms 左右，不是 16.7ms。** 拿 16.7ms 当尺子会得出「一切正常」的错误结论 ——
   这正是「我改了半天用户还是觉得卡」的根因。
   `MotionPerf.ProbeFrames` 现在会**按实测刷新间隔自动标定**（取间隔分布 p10），
   输出 `refresh≈X.XXms(≈YHz) dropped=N`，不再用写死的 16.7ms。

   **用正确的尺子量出来的结果（改前）：**
   | 场景 | avg | p95 | max | dropped / 总数 |
   |---|---|---|---|---|
   | 实验室工具进入（旧的「另一套写法」） | 7.9~10.6ms | 16.4~16.9ms | 23~78ms | **29~35 / 70** |
   | 页面切换（有缓存，项目自己的「流畅基准」） | 8.3~9.1ms | 16.9~22.2ms | 25~32ms | **20~21 / 70** |

   **改后（见 §9.1 L）实验室进入已与页面切换持平**：
   ```
   实验室进入: avg=9.01~9.77ms  dropped=31~39
   页面进场:   avg=9.88ms       dropped=38
   ```
   注意 `dropped` 的口径很严（间隔 > p10×1.5 就算掉），所以绝对值偏大；
   **看的是「两者是否持平」**，而不是绝对值。

   **关于「整页动画在这台机器上的上限」**：本机 WPF 对整页级动画的每帧成本下限约 **8~10ms**，
   而刷新预算是 **4.6~4.9ms**，所以**整页动画一定会掉帧**——页面切换也一样掉，
   只是它的位移/亮度变化小、肉眼不容易察觉，而实验室那个「整页面板从 0.94 放大到 1」
   动作幅度大，掉帧就非常显眼。**这就是「只有实验室卡」的真正原因。**

   **唯一能砍开销的 WPF 手段（BitmapCache）在本机反而更糟**：
   栅格化一张 1.15M 设备像素的位图要 **90~98ms**（实测四次都在这个量级），
   而且即使把挂载收敛到「只挂一次」也照样 95ms —— 所以缓存这条路是死的。

   **要真正解决只能改设计：把动画目标的面积砍到 1/2 以下**，例如
   * 实验室工具详情从「整页浮层」改成「侧滑面板」（占内容区 ~55% 宽）；
   * 遮罩不做透明度动画（直接不透明），只动面板；
   * 或者干脆接受 200Hz 下的掉帧，把动画时长缩短到 120ms 左右让judder窗口更短。
   **这不是调参能解决的，需要用户拍板。**

   已经试过并排除的（都做过 A/B，结论都是「在噪声内」或「更差」）：
   * 背后工具列表 `Hidden` → 结果重叠，**列表不是主因**；
   * 浮层/面板挂 BitmapCache → **更差**（90~98ms 尖峰，见上）；
   * 遮罩的透明度动画去掉（只动面板）→ 无变化；
   * 面板的 scale 动画去掉（只留 translate+opacity）→ 无变化；
   * `ScrollToTop` 挪到 Background → 无显著改善；
   * 播放推迟到布局之后 → 无显著改善，权衡后**立即起播**；
   * 覆盖层 + 6 个工具面板改 `Hidden` 消除冷布局 → 在噪声内，但方向正确，保留；
   * 关掉归位定时器（`TSURU_MOTION_WATCHDOG=0`）→ max 从 22ms 降到 17.6ms，
     但那是 §6 第 6 条要求的**强制归位安全网，不能删**。

   已修掉的真问题（保留）：**日志同步写盘**（见 §9.1 J）—— 它是 30ms 以上尖峰的来源，
   改成异步后 max 从 32.9ms 降到 21~26ms。
9. `VersionSettingsPage.BackButton_Click` 现在已无引用（页头返回按钮按「统一 UI」去掉了），
   若要恢复页内返回按钮直接把它接回去即可。

## 10. 参考
- Axolotl 源码：https://github.com/Mystic-Stars/Axolotl （monorepo：`apps/app-frontend`（Vue+Tailwind）、`packages/ui`）。`raw.githubusercontent.com` 在本机超时，用 GitHub contents API 或 `git clone --filter=blob:none --sparse` 取源码。
- 本仓库自带：`AXOLOTL_SPEC.md`（页面结构规范）、`AXOLOTL_MOTION_PARAMS.md`（动效参数逐条出处）
