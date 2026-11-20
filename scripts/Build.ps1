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
    # COM hosts must declare their actual architecture in the PE header
    foreach ($architecture in @('x86','x64')) {
        & $dotnet build src\Win10ExplorerKit.HashHost\Win10ExplorerKit.HashHost.csproj -c $Configuration --nologo "-p:HashHostArchitecture=$architecture" @buildArguments
        if ($LASTEXITCODE -ne 0) { throw "Hash host build failed: $architecture" }
        $hostDirectory = Join-Path $root "src\Win10ExplorerKit.HashHost\bin\$Configuration\$architecture\net48"
        $appDirectory = Join-Path $root "src\Win10ExplorerKit\bin\$Configuration\net48"
        foreach ($suffix in @('exe','exe.config')) {
            Copy-Item -LiteralPath (Join-Path $hostDirectory "Win10ExplorerKit.HashHost.$architecture.$suffix") -Destination $appDirectory
        }
    }
} finally { Pop-Location }
