<#
.SYNOPSIS
Builds the solution without changing the running installation
.PARAMETER Configuration
MSBuild configuration, Release by default
.PARAMETER BuildRevision
Optional package revision override without changing the saved counter
#>
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [ValidateRange(-1,65534)][int]$BuildRevision = -1
)
. (Join-Path $PSScriptRoot 'Common.ps1')
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Get-DotNetSdk
Push-Location $root
try {
    $buildArguments = @()
    if ($BuildRevision -ge 0) { $buildArguments += "-p:BuildRevision=$BuildRevision" }
    & $dotnet build Win10ExplorerKit.sln -c $Configuration --nologo @buildArguments
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
} finally { Pop-Location }
