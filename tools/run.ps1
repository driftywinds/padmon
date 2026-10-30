param(
    [switch] $Background,
    [switch] $Simulate
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root 'src\Padmon.App\bin\x64\Debug\net10.0-windows10.0.22621.0\win-x64\Padmon.exe'

if (-not (Test-Path $exe)) {
    & (Join-Path $PSScriptRoot 'build.ps1') -Configuration Debug
}

if ($Simulate) {
    $env:PADMON_SIMULATE = '1'
}

$arguments = @()
if ($Background) {
    $arguments += '--background'
}

Start-Process -FilePath $exe -ArgumentList $arguments
