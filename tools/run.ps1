param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Debug',
    [switch] $Background,
    [switch] $Simulate,
    [switch] $SharedData
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "src\Padmon.App\bin\x64\$Configuration\net10.0-windows10.0.22621.0\win-x64\Padmon.exe"

if (-not (Test-Path $exe)) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
}

# Development builds run inside the DSH workspace sandbox. Keep their settings,
# history, and logs under the repository so the sandbox can write them; using
# %LOCALAPPDATA%\padmon here leaves the app stuck at "Starting" with no log.
if (-not $SharedData -and [string]::IsNullOrWhiteSpace($env:PADMON_DATA_DIR)) {
    $env:PADMON_DATA_DIR = Join-Path $root '.tmp\devdata'
    New-Item -ItemType Directory -Force $env:PADMON_DATA_DIR | Out-Null
}

if ($Simulate) {
    $env:PADMON_SIMULATE = '1'
}

$arguments = @()
if ($Background) {
    $arguments += '--background'
}

Start-Process -FilePath $exe -ArgumentList $arguments
