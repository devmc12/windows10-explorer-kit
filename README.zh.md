<p align="center"><img src="src/Win10ExplorerKit/Resources/app.png" width="96" alt="Win10 Explorer Kit 图标"></p>

<h1 align="center">Win10 Explorer Kit</h1>

<p align="center"><strong><a href="README.md">English</a> | 简体中文</strong></p>

为 Windows 10 文件资源管理器提供路径复制、文件夹恢复与文件哈希计算功能。

- 右键菜单或 **Ctrl + Shift + C** 复制路径，支持多选和双引号设置
- 自动记住打开的文件夹，通过托盘菜单恢复
- 自定义固定文件夹列表，一键批量打开
- 右键或拖入文件计算 MD5、SHA-1、SHA-256 或 SHA-512
- 支持英文、简体中文切换和可配置的登录自启
- 支持跟随系统、浅色和深色主题

## 安装使用

需要 **Windows 10 x86/x64** 和 [.NET Framework 4.8 或更新版本](https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/net48)。从 GitHub Releases 下载：

| 文件 | 用法 |
| --- | --- |
| `Win10-Explorer-Kit-v<version>-Windows-Setup.exe` | 运行安装向导 |
| `Win10-Explorer-Kit-v<version>-Windows-Portable.zip` | 完整解压到固定目录，双击 `Win10ExplorerKit.exe` |

ZIP 版首次启动会自动注册右键菜单和登录自启。

选中文件后，右键选择 **复制路径 (Ctrl+Shift+C)** 或按 **Ctrl + Shift + C**。左键点击托盘图标打开窗口，右键打开菜单。

## 配置与卸载

用户配置保存在程序目录，覆盖安装会保留已有配置。

- **安装版**：通过 Windows 设置卸载。
- **ZIP 版**：先运行 `Uninstall.cmd`，成功后删除解压文件夹。

两种方式都会移除注册项并清理用户配置。

## 开发与打包

安装 **.NET 10 SDK** 和 **Inno Setup 6.7.4 或更新版本**，使用 IDE 打开 `Win10ExplorerKit.sln`，或运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1 -Platform x86
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package.ps1
```

找不到安装包编译器时，给打包脚本传入 `-InnoCompiler '<ISCC.exe路径>'`。构建不会替换或停止已经安装的程序。

## 项目结构

```text
windows10-explorer-kit/                     # 项目根目录
├── src/Win10ExplorerKit/                   # WPF 桌面应用
│   ├── packages.lock.json                  # 依赖版本锁定
│   ├── Win10ExplorerKit.csproj             # 应用工程与运行时目标
│   ├── App.xaml                            # WPF UI 主题与共享资源
│   ├── Program.cs                          # 命令分发与程序入口
│   ├── app.manifest                        # Windows 权限与 DPI 声明
│   ├── App/                                # 生命周期、单实例与数据清理
│   ├── Configuration/                      # 配置模型与 INI 读写
│   ├── Diagnostics/                        # 应用日志
│   ├── Features/                           # 路径复制、文件夹恢复与哈希计算
│   ├── Windows/                            # 资源管理器、自启、热键及原生 API
│   ├── UI/                                 # WPF 页面、视图模型与托盘
│   ├── Localization/                       # 翻译服务与 JSON 语言文件
│   └── Resources/                          # 应用图标与 Config.example.ini 示例
├── src/Win10ExplorerKit.HashHost/          # x86/x64 COM 选择转发程序
├── tests/                                  # 自动化测试与集成检查
│   ├── Win10ExplorerKit.Tests/             # 隔离的单元与视图模型测试
│   └── ShellSmoke/                         # 原生菜单与 x64/x86 COM 检查
├── scripts/                                # 构建工具
│   ├── Common.ps1                          # 公共 SDK 查找逻辑
│   ├── Build.ps1                           # 编译解决方案
│   ├── Test.ps1                            # 执行 x64 或 x86 进程测试
│   └── Package.ps1                         # 生成发布包与 SHA-256 校验文件
├── packaging/                              # 发布配置
│   ├── inno/Win10ExplorerKit.iss           # 安装与卸载逻辑
│   ├── inno/Languages/                     # 第三方安装器语言包
│   ├── inno/INNO-LICENSE.txt               # Inno Setup 许可证
│   ├── licenses/                           # 界面与运行库许可证
│   └── portable/                           # 解压版标记与取消注册清理脚本
├── artifacts/<version>/                    # 发布产物，不纳入 Git
├── Win10ExplorerKit.sln                    # 包含应用和测试的解决方案
├── Directory.Build.props                   # 公共构建配置与统一版本
├── global.json                             # .NET SDK 选择规则
├── NuGet.Config                            # 依赖包还原来源
├── .gitignore                              # 本地文件与构建产物忽略规则
├── AGENTS.md                               # 开发与维护约定
├── README.md / README.zh.md                # 英文与中文说明
├── CHANGELOG.md                            # 版本发布记录
├── LICENSE                                 # 项目 MIT 许可证
└── THIRD-PARTY-NOTICES.md                  # 第三方署名与许可说明
```

版本和发布者在 `Directory.Build.props` 中配置，构建号随打包自动递增。

## 手动发布

运行 `Package.ps1`，检查产物后，在 GitHub 创建 Release，标签使用 `v<version>` 格式，与 `Directory.Build.props` 中的版本一致。使用 [CHANGELOG.md](CHANGELOG.md) 填写说明，手动上传对应产物目录中的 **Setup.exe**、**Portable.zip** 和 **SHA256SUMS.txt**。打包脚本不会提交、推送、创建标签或上传文件。同一版本重复打包会覆盖 `artifacts/<version>/` 中的产物，临时打包文件会自动清理。
