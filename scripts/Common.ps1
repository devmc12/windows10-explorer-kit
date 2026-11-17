<#
.SYNOPSIS
Shared build-tool discovery without machine-specific paths
#>
$ErrorActionPreference = 'Stop'

function Get-DotNetSdk {
    <#
    .SYNOPSIS
    Finds a dotnet host with an installed SDK using PATH and DOTNET_ROOT
    .OUTPUTS
    Absolute executable path
    #>
    $candidates = @()
    if ($env:DOTNET_ROOT) { $candidates += Join-Path $env:DOTNET_ROOT 'dotnet.exe' }
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($command) { $candidates += $command.Source }
    foreach ($candidate in ($candidates | Select-Object -Unique)) {
        if ((Test-Path -LiteralPath $candidate) -and (& $candidate --list-sdks)) { return $candidate }
    }
    throw 'Install the .NET 10 SDK or set DOTNET_ROOT to an SDK installation'
}
