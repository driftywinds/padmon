param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root 'eng\dotnet.ps1'

& $dotnet restore (Join-Path $root 'Padmon.sln') `
    --property:Platform=x64 `
    --property:RestoreUseStaticGraphEvaluation=true
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $dotnet build (Join-Path $root 'Padmon.sln') `
    --configuration $Configuration `
    --no-restore `
    --property:Platform=x64 `
    --property:UseSharedCompilation=false `
    --maxcpucount:1
exit $LASTEXITCODE
