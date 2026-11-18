<p align="center"><img src="src/Win10ExplorerKit/Resources/app.png" width="96" alt="Win10 Explorer Kit icon"></p>

<h1 align="center">Win10 Explorer Kit</h1>

<p align="center"><strong>English | <a href="README.zh.md">简体中文</a></strong></p>

Path copying and folder recovery for Windows 10 File Explorer.

- Copy paths from the context menu or with **Ctrl + Shift + C**, with multi-selection and optional double quotes
- Automatically remember open folders and restore them from the tray menu or by double-clicking the tray icon
- Open a custom list of fixed folders in one action
- Switch between English and Simplified Chinese and configure login startup

## Install

Requires **Windows 10 x86/x64** and [.NET Framework 4.8 or later](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48). Download from GitHub Releases:

| File | Usage |
| --- | --- |
| `Win10-Explorer-Kit-v<version>-Windows-Setup.exe` | Run the setup wizard |
| `Win10-Explorer-Kit-v<version>-Windows-Portable.zip` | Extract all files to a permanent folder and double-click `Win10ExplorerKit.exe` |

The ZIP edition registers the Explorer menu and login startup on its first launch.

Select files, then choose **Copy path (Ctrl+Shift+C)** from the context menu or press **Ctrl + Shift + C**. Left-click or right-click the tray icon to open its menu.

## Settings and uninstall

User settings are stored in the application folder and preserved during upgrades.

- **Setup edition**: uninstall through Windows Settings.
- **ZIP edition**: run `Uninstall.cmd`, then delete the extracted folder after it succeeds.

Both methods remove integration entries and user settings.

## Develop and package

Install the **.NET 10 SDK** and **Inno Setup 6.7.4 or later**. Open `Win10ExplorerKit.sln` in an IDE, or run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Build.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Test.ps1 -Platform x86
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Package.ps1
```

If needed, pass `-InnoCompiler '<path-to-ISCC.exe>'` to the packaging script. Building does not replace or stop an installed application.

## Project structure

```text
windows10-explorer-kit/                       # Repository root
├── src/Win10ExplorerKit/                     # Windows desktop application
│   ├── Win10ExplorerKit.csproj               # Application project and runtime target
│   ├── Program.cs                            # Command routing and application entry
│   ├── app.manifest                          # Windows permissions and DPI settings
│   ├── App/                                  # Lifetime, single instance and cleanup
│   ├── Configuration/                        # Typed settings and INI persistence
│   ├── Diagnostics/                          # Application logging
│   ├── Features/                             # Path copying and folder recovery
│   ├── Windows/                              # Explorer, startup, hotkeys and native APIs
│   ├── UI/                                   # Tray menus and settings forms
│   ├── Localization/                         # Translation service and JSON locales
│   └── Resources/                            # App icons and Config.example.ini
├── tests/Win10ExplorerKit.Tests/             # Isolated automated tests and fixtures
├── scripts/                                  # Build tooling
│   ├── Common.ps1                            # Shared SDK discovery
│   ├── Build.ps1                             # Build the solution
│   ├── Test.ps1                              # Run tests in x64 or x86 processes
│   └── Package.ps1                           # Create packages and SHA-256 checksums
├── packaging/                                # Distribution definitions
│   ├── inno/Win10ExplorerKit.iss             # Setup and uninstall behavior
│   ├── inno/Languages/                       # Upstream installer translations
│   ├── inno/INNO-LICENSE.txt                 # Inno Setup license
│   └── portable/                             # Portable marker and cleanup script
├── artifacts/<version>/                      # Generated release files, ignored by Git
├── Win10ExplorerKit.sln                      # Solution containing app and tests
├── Directory.Build.props                     # Shared build settings and version
├── global.json                               # .NET SDK selection
├── NuGet.Config                              # Package restore sources
├── .gitignore                                # Local files and build output exclusions
├── AGENTS.md                                 # Development and maintenance conventions
├── README.md / README.zh.md                  # English and Chinese documentation
├── CHANGELOG.md                              # Release history
├── LICENSE                                   # Project MIT license
└── THIRD-PARTY-NOTICES.md                    # Third-party attribution and licenses
```

Configure the version and publisher in `Directory.Build.props`; the build number increments automatically when packaging.

## Release manually

Run `Package.ps1`, review the generated files, then create a GitHub Release with a tag in the form `v<version>`, matching `Directory.Build.props`. Use [CHANGELOG.md](CHANGELOG.md) for the release notes and upload **Setup.exe**, **Portable.zip** and **SHA256SUMS.txt** from the corresponding output folder. Packaging does not commit, push, create tags or upload files. Repackaging the same version overwrites the files in `artifacts/<version>/`; temporary packaging files are cleaned automatically.
