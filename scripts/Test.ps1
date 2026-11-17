<#
.SYNOPSIS
Runs automated tests with isolated temporary data
.PARAMETER Platform
Test process architecture on Windows, x64 by default
.PARAMETER BuildRevision
Optional package revision override used when tests rebuild the application
#>
param(
    [ValidateSet('x86','x64')][string]$Platform = 'x64',
    [ValidateRange(-1,65534)][int]$BuildRevision = -1
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Get-DotNetSdk
Push-Location $root
try {
    $buildArguments = @()
    if ($BuildRevision -ge 0) { $buildArguments += "-p:BuildRevision=$BuildRevision" }
    & $dotnet test tests\Win10ExplorerKit.Tests\Win10ExplorerKit.Tests.csproj -c Release --nologo @buildArguments --logger 'trx' --results-directory .work\test-results -- RunConfiguration.TargetPlatform=$Platform
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
} finally { Pop-Location }
