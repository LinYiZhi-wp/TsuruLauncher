# Tsuru Launcher

一个基于 .NET 8 + WPF 的现代化 Minecraft 启动器，支持游戏版本管理、模组资源下载、微软账号登录等功能。

> 原名 **LYZL Minecraft Launcher**（v1.x），自 **v2.0** 起更名为 **Tsuru Launcher**。

## 功能特性

- **游戏版本管理**：下载、安装、管理多个 Minecraft 版本，支持版本隔离
- **资源管理**：集成 Modrinth，支持浏览、搜索和下载模组、资源包、模组包、光影、数据包
- **资源详情页**：描述 / 版本 / 图库三个标签页，版本表格支持筛选、排序、分页
- **账号系统**：支持微软账号登录认证
- **下载管理**：多任务下载队列，支持断点续传和进度追踪
- **现代化 UI**：iOS26 风格毛玻璃效果，统一颜色系统，流畅过渡动画
- **多语言**：支持中文和英文切换
- **Java 管理**：自动检测和配置 Java 运行环境
- **游戏导出**：支持导出游戏实例配置

## 技术栈

| 技术 | 用途 |
|------|------|
| .NET 8 | 运行时框架 |
| WPF | 桌面 UI 框架 |
| WPF-UI | 现代化 UI 控件库 |
| CommunityToolkit.Mvvm | MVVM 架构 |
| Newtonsoft.Json | JSON 序列化 |
| SharpZipLib | 压缩文件处理 |
| WebView2 | 网页内嵌组件 |

## 项目结构

```
TsuruLauncher/
├── Assets/           # 静态资源
├── Controls/         # 自定义控件
├── Converters/       # 数据绑定转换器
├── Models/           # 数据模型
│   └── Ecosystem/    # 模组生态模型
├── Resources/        # 多语言资源
├── Services/         # 业务服务层
│   ├── Animation/    # 动画服务
│   ├── Ecosystem/    # 模组生态服务
│   └── Network/      # 网络服务
├── Styles/           # 样式资源
├── ViewModels/       # 视图模型
└── Views/            # 视图页面
    └── Dialogs/      # 对话框
```

## 构建运行

### 环境要求
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 或更高版本

### 构建
```bash
git clone https://github.com/LinYiZhi-wp/TsuruLauncher.git
cd TsuruLauncher
dotnet build
```

### 运行
```bash
dotnet run
```

## 版本

- **v2.0.0** - 更名为 Tsuru Launcher；资源详情页重构（描述 / 版本 / 图库三标签）、
  统一弹框样式（iOS26Dialog）、资源包 / 光影 / 数据包分类支持、单滚动条布局
- **v1.2.0** - 优化下载管理、ModLoader、Modpack 服务，改进动画过渡和版本设置页面
- **v1.1.0** - UI 全面升级：统一颜色系统、毛玻璃效果、标准化圆角、修复 UI 崩溃问题

### v2.0 迁移说明

- 项目目录由 `GeminiLauncher` 改名为 `TsuruLauncher`
- 每个游戏版本的配置文件由 `lyzl_profile.json` 改名为 `tsuru_profile.json`
  （首次保存时自动迁移，旧文件会被删除；读取时若只有旧文件仍可正常加载，设置不会丢失）

## 鸣谢

- [Modrinth](https://modrinth.com/) - 模组资源 API
- [Microsoft](https://www.microsoft.com/) - 微软账号认证 API
- [Mojang](https://www.minecraft.net/) - Minecraft 版本清单 API
- [WPF-UI](https://github.com/lepoco/wpfui) - 现代化 WPF 控件库
