<#
.SYNOPSIS
Checks real registered hash-host cold starts from x64 and x86 Shell clients
.PARAMETER ApplicationDirectory
Directory of the installed or registered portable application
.PARAMETER Count
Number of individual files alongside a first-level folder selection
#>
param(
    [Parameter(Mandatory=$true)][string]$ApplicationDirectory,
    [ValidateRange(1,10000)][int]$Count = 3
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$application = Join-Path $ApplicationDirectory 'Win10ExplorerKit.exe'
$architecture = if ([Environment]::Is64BitOperatingSystem) { 'x64' } else { 'x86' }
$hostPath = [IO.Path]::GetFullPath((Join-Path $ApplicationDirectory "Win10ExplorerKit.HashHost.$architecture.exe"))
$hostName = [IO.Path]::GetFileNameWithoutExtension($hostPath)
if (Get-Process -Name $hostName -ErrorAction SilentlyContinue) { throw 'Close the hash window and wait for its process to exit before this cold-start test' }
# Fixtures and compiled clients never enter the application directory
$workspace = Join-Path $root ('.work\installed-smoke-' + [Guid]::NewGuid().ToString('N'))
$fixtures = Join-Path $workspace 'fixtures'
New-Item -ItemType Directory -Path $fixtures -Force | Out-Null
$paths = @(1..$Count | ForEach-Object {
    $path = Join-Path $fixtures ("file-{0:D4}.txt" -f $_)
    [IO.File]::WriteAllText($path,'abc')
    $path
})
$folder = Join-Path $fixtures 'folder'
New-Item -ItemType Directory -Path (Join-Path $folder 'nested') -Force | Out-Null
[IO.File]::WriteAllText((Join-Path $folder 'one.txt'),'one')
[IO.File]::WriteAllText((Join-Path $folder 'two.txt'),'two')
[IO.File]::WriteAllText((Join-Path $folder 'nested\excluded.txt'),'excluded')
$manifest = Join-Path $workspace 'selection.txt'
[IO.File]::WriteAllLines($manifest, $paths + @($folder))
$platforms = if ([Environment]::Is64BitOperatingSystem) { @('x64','x86') } else { @('x86') }
foreach ($platform in $platforms) {
    $destination = Join-Path $workspace $platform
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath $application -Destination $destination
    Copy-Item -LiteralPath ($application + '.config') -Destination (Join-Path $destination 'InstalledClient.exe.config')
    $framework = if ([Environment]::Is64BitOperatingSystem) { 'Framework64' } else { 'Framework' }
    $compiler = Join-Path $env:WINDIR "Microsoft.NET\$framework\v4.0.30319\csc.exe"
    $client = Join-Path $destination 'InstalledClient.exe'
    & $compiler /nologo /target:exe "/platform:$platform" "/out:$client" "/reference:$application" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'InstalledClient.cs') (Join-Path $PSScriptRoot 'MenuClient.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Client compilation failed' }
    & $client $manifest ($Count + 2) $hostPath
    if ($LASTEXITCODE -ne 0) { throw "Installed cold-start check failed: $platform" }
}
