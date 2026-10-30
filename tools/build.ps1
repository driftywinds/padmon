param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

& (Join-Path $root 'eng\dotnet.ps1') build (Join-Path $root 'Padmon.sln') `
    --configuration $Configuration `
    --property:Platform=x64 `
    --property:UseSharedCompilation=false `
    --maxcpucount:1
