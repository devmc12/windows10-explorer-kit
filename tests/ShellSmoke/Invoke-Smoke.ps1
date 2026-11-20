<#
.SYNOPSIS
Checks out-of-process Shell selection marshaling with isolated x64 and x86 clients
.DESCRIPTION
Build the application first. Uses a temporary COM class and independent host role,
never the application's real registration, startup entries or personal settings.
Reports and a window image are retained under .work/shell-smoke-*.
.PARAMETER Count
Number of individual files in the first selection
.PARAMETER Language
Locale used for the test window
#>
param([ValidateRange(1, 10000)][int]$Count = 1000, [ValidateSet('en','zh-CN')][string]$Language = 'en')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$build = Join-Path $root 'src\Win10ExplorerKit\bin\Release\net48'
if (-not (Test-Path -LiteralPath (Join-Path $build 'Win10ExplorerKit.exe'))) { throw 'Run scripts/Build.ps1 first' }
# A random COM identity keeps this run independent of installed applications
$classId = [Guid]::NewGuid().ToString('B')
$workspace = Join-Path $root ('.work\shell-smoke-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $workspace -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $workspace 'Config.ini'), "[General]`r`nLanguage=$Language`r`n")
$registryPath = 'Software\Classes\CLSID\' + $classId
$verb = 'ExplorerKitHashSmoke' + ([Guid]$classId).ToString('N')
# Keep each path a separate array element so cleanup owns the complete top-level verb
[string[]]$menuPaths = @(
    "Software\Classes\*\shell\$verb"
    "Software\Classes\Directory\shell\$verb"
)
foreach ($menuPath in $menuPaths) {
    if (($menuPath -split '\\')[-1] -ne $verb -or ($menuPath -split '\\').Count -ne 5) {
        throw 'Unexpected smoke-menu registration path'
    }
}
$server = $null
try {
    foreach ($platform in @('x64', 'x86')) {
        $destination = Join-Path $workspace $platform
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $build 'Win10ExplorerKit.exe') -Destination $destination
        Copy-Item -LiteralPath (Join-Path $build 'Win10ExplorerKit.exe.config') -Destination (Join-Path $destination 'Win10ExplorerKit.Tests.exe.config')
        $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
        & $compiler /nologo /target:exe "/platform:$platform" "/out:$destination\Win10ExplorerKit.Tests.exe" "/reference:$build\Win10ExplorerKit.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'Host.cs') (Join-Path $PSScriptRoot 'MenuClient.cs')
        if ($LASTEXITCODE -ne 0) { throw 'Smoke host compilation failed' }
    }
    $server = Join-Path $workspace 'x64\Win10ExplorerKit.Tests.exe'
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($registryPath + '\LocalServer32')
    try { $key.SetValue('', ('"{0}" server {1} "{2}"' -f $server,$classId,$workspace)) }
    finally { $key.Dispose() }
    $fixtures = Join-Path $workspace 'fixtures'
    New-Item -ItemType Directory -Path $fixtures -Force | Out-Null
    $paths = 1..$Count | ForEach-Object {
        $path = Join-Path $fixtures ("file-{0:D4}.txt" -f $_)
        [IO.File]::WriteAllText($path, 'abc')
        $path
    }
    $manifest = Join-Path $workspace 'selection.txt'
    [IO.File]::WriteAllLines($manifest, $paths)
    & $server client $classId $workspace $manifest $Count
    if ($LASTEXITCODE -ne 0) { throw 'x64 COM activation or selection failed' }
    $initialProcess = ([IO.File]::ReadAllText((Join-Path $workspace 'state.txt')) -split ',')[0]
    $extra = Join-Path $fixtures 'extra.txt'
    [IO.File]::WriteAllText($extra, 'def')
    [IO.File]::WriteAllLines($manifest, @($paths) + @($extra))
    & (Join-Path $workspace 'x86\Win10ExplorerKit.Tests.exe') client $classId $workspace $manifest ($Count + 1)
    if ($LASTEXITCODE -ne 0) { throw 'x86 COM activation or selection failed' }
    if (([IO.File]::ReadAllText((Join-Path $workspace 'state.txt')) -split ',')[0] -ne $initialProcess) { throw 'Repeated activation created a different host' }
    foreach ($menuPath in $menuPaths) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($menuPath)
        try {
            $key.SetValue('MUIVerb', 'Explorer Kit hash smoke test')
            $key.SetValue('MultiSelectModel', 'Player')
            $command = $key.CreateSubKey('command')
            try { $command.SetValue('DelegateExecute', $classId) } finally { $command.Dispose() }
        } finally { $key.Dispose() }
    }
    $folder = Join-Path $fixtures 'folder'
    New-Item -ItemType Directory -Path (Join-Path $folder 'nested') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $folder 'one.txt'), 'one')
    [IO.File]::WriteAllText((Join-Path $folder 'two.txt'), 'two')
    [IO.File]::WriteAllText((Join-Path $folder 'nested\excluded.txt'), 'excluded')
    [IO.File]::WriteAllLines($manifest, @($paths) + @($extra, $folder))
    & $server menu $classId $workspace $manifest ($Count + 3)
    if ($LASTEXITCODE -ne 0) { throw 'Native mixed-selection context menu failed' }
    $hostProcess = Get-Process -Id $initialProcess
    Write-Host ("Hash host private memory: {0:N1} MiB" -f ($hostProcess.PrivateMemorySize64 / 1MB))
    Write-Host "Shell smoke passed. Reports: $workspace"
} finally {
    # Unregister even when the test host fails to shut down
    try {
        if ($server -and (Test-Path -LiteralPath $server)) { & $server stop $classId $workspace }
    } finally {
        foreach ($ownedPath in @($menuPaths) + @($registryPath)) {
            try { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($ownedPath, $false) }
            catch { Write-Warning "Could not remove test registration: $ownedPath" }
        }
    }
}
