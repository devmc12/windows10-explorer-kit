# Third-party notices

Win10 Explorer Kit source code is licensed under MIT. The .NET Framework runtime
is provided by Microsoft and is not bundled in these packages.

The Setup.exe installer is generated with [Inno Setup](https://jrsoftware.org/).
Copyright (C) 1997-2026 Jordan Russell; portions Copyright (C) 2000-2026 Martijn Laan.
Its license is preserved in `packaging/inno/INNO-LICENSE.txt` in the source tree
and `licenses/Inno-Setup.txt` in the distribution packages.

The Simplified Chinese installer translation was obtained from the Inno Setup
6.7.4 source distribution, `Files/Languages/Unofficial/ChineseSimplified.isl`.
The translation is included unchanged, with its original maintainer attribution,
contact email addresses and source links. It remains subject to its upstream
notices and is not relicensed by the project's MIT license.

The interface uses [WPF UI](https://github.com/lepoco/wpfui), including its
Abstractions library and bundled Fluent icons. Its MIT license and upstream
third-party notices are preserved in `packaging/licenses/WPF-UI-*.txt` and copied
to `licenses/` in the distribution packages.

System.Memory, System.Buffers, System.Numerics.Vectors and
System.Runtime.CompilerServices.Unsafe are distributed under the upstream
[.NET maintenance packages](https://github.com/dotnet/maintenance-packages)
MIT license. The license is preserved in
`packaging/licenses/DotNet-LICENSE.txt` and copied to `licenses/` in the packages.

The application icon was generated for this project. NuGet reference assemblies
and MSTest dependencies are used only for building/testing and are not included
in the application distribution.
