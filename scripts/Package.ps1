<#
.SYNOPSIS
Builds versioned Setup.exe and Portable.zip artifacts for manual release upload
.PARAMETER InnoCompiler
Optional path to a locally installed ISCC.exe
.PARAMETER SkipTests
Builds packages without running the automated test suites
#>
param([string]$InnoCompiler, [switch]$SkipTests)
. (Join-Path $PSScriptRoot 'Common.ps1')
$root = Split-Path -Parent $PSScriptRoot
# Hold the counter lock for the entire build so concurrent packages cannot reuse a revision
$workRoot = Join-Path $root '.work'
New-Item -ItemType Directory -Path $workRoot -Force | Out-Null
$packageLock = [System.IO.File]::Open((Join-Path $workRoot 'package.lock'), [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
try {
    $propertiesPath = Join-Path $root 'Directory.Build.props'
    $originalProperties = [System.IO.File]::ReadAllText($propertiesPath)
    [xml]$properties = $originalProperties
    $version = [string]$properties.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must contain three numeric components' }
    $savedRevision = [string]$properties.Project.PropertyGroup.BuildRevision
    if ($savedRevision -notmatch '^\d+$' -or [long]$savedRevision -ge 65534) { throw 'BuildRevision must be an integer between 0 and 65533 before packaging' }
    $revision = [int]$savedRevision + 1
    $fileVersion = "$version.$revision"
    $nextProperties = $originalProperties.Replace("<BuildRevision>$savedRevision</BuildRevision>", "<BuildRevision>$revision</BuildRevision>")
    if ($nextProperties -eq $originalProperties) { throw 'Cannot update BuildRevision in Directory.Build.props' }
    $company = [string]$properties.Project.PropertyGroup.Company
    if ([string]::IsNullOrWhiteSpace($company)) { throw 'Set Company in Directory.Build.props before packaging' }
    $name = "Win10-Explorer-Kit-v$version-Windows"
    $artifacts = Join-Path $root 'artifacts'
    # Temporary files stay outside the release tree and are removed on completion or failure
    $workspace = Join-Path $workRoot ('package-' + [Guid]::NewGuid().ToString('N'))
    $stage = Join-Path $workspace 'payload'
    $output = Join-Path $workspace 'output'
    $release = Join-Path $artifacts $version
    New-Item -ItemType Directory -Path $stage,$output -Force | Out-Null
    try {
        Write-Host "Packaging build $fileVersion"
        & (Join-Path $PSScriptRoot 'Build.ps1') -BuildRevision $revision
        if (-not $SkipTests) {
            & (Join-Path $PSScriptRoot 'Test.ps1') -BuildRevision $revision
            & (Join-Path $PSScriptRoot 'Test.ps1') -Platform x86 -BuildRevision $revision
        }
        $build = Join-Path $root 'src\Win10ExplorerKit\bin\Release\net48'
        foreach ($file in @('Win10ExplorerKit.exe','Win10ExplorerKit.exe.config',
            'Win10ExplorerKit.HashHost.x86.exe','Win10ExplorerKit.HashHost.x86.exe.config',
            'Win10ExplorerKit.HashHost.x64.exe','Win10ExplorerKit.HashHost.x64.exe.config')) {
            Copy-Item -LiteralPath (Join-Path $build $file) -Destination $stage
        }
        # Ship only the runtime libraries resolved by the locked WPF dependency graph
        foreach ($file in @('Wpf.Ui.dll','Wpf.Ui.Abstractions.dll','System.Memory.dll',
            'System.Buffers.dll','System.Numerics.Vectors.dll','System.Runtime.CompilerServices.Unsafe.dll')) {
            Copy-Item -LiteralPath (Join-Path $build $file) -Destination $stage
        }
        $applicationInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $build 'Win10ExplorerKit.exe'))
        if ($applicationInfo.FileVersion -ne $fileVersion -or $applicationInfo.ProductVersion -ne $fileVersion) {
            throw 'Application file version does not match Directory.Build.props'
        }
        if ($applicationInfo.CompanyName -ne $company) {
            throw 'Application company does not match Directory.Build.props'
        }
        # Reject AnyCPU output for the 64-bit COM host before publishing a broken package
        foreach ($architecture in @('x86','x64')) {
            $hostPath = Join-Path $stage "Win10ExplorerKit.HashHost.$architecture.exe"
            $hostInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($hostPath)
            if ($hostInfo.FileVersion -ne $fileVersion -or $hostInfo.CompanyName -ne $company) {
                throw "Hash host metadata differs from the application: $architecture"
            }
            $bytes = [IO.File]::ReadAllBytes($hostPath)
            $peOffset = [BitConverter]::ToInt32($bytes, 0x3c)
            $machine = [BitConverter]::ToUInt16($bytes, $peOffset + 4)
            $expectedMachine = if ($architecture -eq 'x64') { 0x8664 } else { 0x014c }
            if ($machine -ne $expectedMachine) { throw "Incorrect COM host architecture: $architecture" }
        }
        New-Item -ItemType Directory -Path (Join-Path $stage 'locales'),(Join-Path $stage 'licenses') -Force | Out-Null
        foreach ($locale in @('en.json','zh-CN.json')) {
            Copy-Item -LiteralPath (Join-Path $build "locales\$locale") -Destination (Join-Path $stage 'locales')
        }
        Copy-Item -LiteralPath (Join-Path $root 'packaging\inno\INNO-LICENSE.txt') -Destination (Join-Path $stage 'licenses\Inno-Setup.txt')
        Get-ChildItem -LiteralPath (Join-Path $root 'packaging\licenses') -Filter '*.txt' |
            Copy-Item -Destination (Join-Path $stage 'licenses')
        foreach ($file in @('README.md','README.zh.md','CHANGELOG.md','LICENSE','THIRD-PARTY-NOTICES.md')) {
            Copy-Item -LiteralPath (Join-Path $root $file) -Destination $stage
        }
        New-Item -ItemType Directory -Path (Join-Path $stage 'assets') -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $root 'src\Win10ExplorerKit\Resources\app.png') -Destination (Join-Path $stage 'assets')
        foreach ($file in @('README.md','README.zh.md')) {
            $path = Join-Path $stage $file
            $content = [System.IO.File]::ReadAllText($path).Replace('src/Win10ExplorerKit/Resources/app.png', 'assets/app.png')
            [System.IO.File]::WriteAllText($path, $content, (New-Object System.Text.UTF8Encoding($false)))
        }
        foreach ($file in @('Uninstall.cmd','portable.flag')) {
            Copy-Item -LiteralPath (Join-Path $root "packaging\portable\$file") -Destination $stage
        }
        $zip = Join-Path $output "$name-Portable.zip"
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
        if (-not $InnoCompiler) {
            $candidates = @((Join-Path $root '.tools\InnoSetup\ISCC.exe'))
            if (${env:ProgramFiles(x86)}) { $candidates += Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe' }
            if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe' }
            $command = Get-Command ISCC -ErrorAction SilentlyContinue
            if ($command) { $candidates += $command.Source }
            $InnoCompiler = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
        }
        if (-not $InnoCompiler) { throw 'Install Inno Setup 6.7.4+ and provide -InnoCompiler if it is not on PATH' }
        & $InnoCompiler /Qp "/DAppVersion=$version" "/DAppBuildVersion=$fileVersion" "/DAppCompany=$company" "/DSourceDir=$stage" "/DReleaseDir=$output" (Join-Path $root 'packaging\inno\Win10ExplorerKit.iss')
        if ($LASTEXITCODE -ne 0) { throw 'Installer build failed' }
        # Inno Setup pads version-resource strings with spaces
        if ([System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $output "$name-Setup.exe")).CompanyName.Trim() -ne $company) {
            throw 'Installer company does not match Directory.Build.props'
        }
        if ([System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $output "$name-Setup.exe")).FileVersion.Trim() -ne $fileVersion) {
            throw 'Installer file version does not match the package revision'
        }
        # Hash only this release's two known packages, never unrelated files in the output folder
        @((Join-Path $output "$name-Setup.exe"), $zip) | ForEach-Object { Get-FileHash -LiteralPath $_ -Algorithm SHA256 } |
            ForEach-Object { '{0}  {1}' -f $_.Hash.ToLowerInvariant(), (Split-Path -Leaf $_.Path) } |
            Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding ASCII
        # Check existing files before replacement so a known lock does not leave a mixed release
        $files = @("$name-Setup.exe", "$name-Portable.zip", 'SHA256SUMS.txt')
        New-Item -ItemType Directory -Path $release -Force | Out-Null
        foreach ($file in $files) {
            $target = Join-Path $release $file
            if (Test-Path -LiteralPath $target) {
                try {
                    $handle = [System.IO.File]::Open($target, [System.IO.FileMode]::Open, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
                    $handle.Dispose()
                } catch { throw "Cannot replace $file. Close applications using this release and retry" }
            }
        }
        # Persist only successful packages and avoid overwriting edits made during the build
        if ([System.IO.File]::ReadAllText($propertiesPath) -ne $originalProperties) {
            throw 'Directory.Build.props changed during packaging; the revision counter was not saved'
        }
        foreach ($file in $files) {
            Copy-Item -LiteralPath (Join-Path $output $file) -Destination (Join-Path $release $file) -Force
        }
        [System.IO.File]::WriteAllText($propertiesPath, $nextProperties, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "Packaged build: $fileVersion"
        Write-Host "Release files: $release"
    } finally {
        # Validate the exact temporary target before recursive removal
        $resolvedWork = [System.IO.Path]::GetFullPath($workspace)
        $expectedParent = [System.IO.Path]::GetFullPath($workRoot)
        if ([System.IO.Path]::GetDirectoryName($resolvedWork) -ne $expectedParent -or
            [System.IO.Path]::GetFileName($resolvedWork) -notlike 'package-*') {
            throw 'Refusing to remove an unexpected packaging directory'
        }
        if (Test-Path -LiteralPath $resolvedWork) { Remove-Item -LiteralPath $resolvedWork -Recurse -Force }
    }
} finally { $packageLock.Dispose() }
