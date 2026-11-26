<#
.SYNOPSIS
Exercises real NTFS permissions on newly created fixtures with one optional UAC prompt
.DESCRIPTION
Uses the built application's permission service and worker without starting its UI
or changing registration, settings or existing files; reports stay under .work
.PARAMETER Platform
Architecture of the isolated harness and elevated worker
#>
param([ValidateSet('x86', 'x64')][string]$Platform = 'x64')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$build = Join-Path $root 'src\Win10ExplorerKit\bin\Release\net48'
$application = Join-Path $build 'Win10ExplorerKit.exe'
if (-not (Test-Path -LiteralPath $application)) { throw 'Run scripts/Build.ps1 first' }
$workspace = Join-Path $root ('.work\permission-smoke-' + [Guid]::NewGuid().ToString('N'))
$runner = Join-Path $workspace 'runner'
$product = Join-Path $runner 'product'
New-Item -ItemType Directory -Path $product | Out-Null
Copy-Item -LiteralPath $application -Destination $product
Get-ChildItem -LiteralPath $build -Filter '*.dll' | Copy-Item -Destination $product
Copy-Item -LiteralPath ($application + '.config') -Destination (Join-Path $runner 'Win10ExplorerKit.exe.config')
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$compiled = Join-Path $runner 'Win10ExplorerKit.Tests.exe'
& $compiler /nologo /target:exe "/platform:$Platform" "/out:$compiled" "/reference:$application" (Join-Path $PSScriptRoot 'Host.cs')
if ($LASTEXITCODE -ne 0) { throw 'Permission harness compilation failed' }
# Preserve the friend assembly identity while matching the worker's executable-path check
$executable = Join-Path $runner 'Win10ExplorerKit.exe'
Move-Item -LiteralPath $compiled -Destination $executable
& $executable
$result = $LASTEXITCODE
Write-Host "Permission smoke report: $workspace"
if ($result -ne 0) { throw 'Native permission checks failed; inspect report.txt' }
