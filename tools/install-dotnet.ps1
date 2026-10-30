$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$tools = Join-Path $root '.tools'
$install = Join-Path $root '.dotnet'

New-Item -ItemType Directory -Force $tools, $install | Out-Null

$installer = Join-Path $tools 'dotnet-install.ps1'
if (-not (Test-Path $installer)) {
    Write-Host 'Downloading dotnet-install.ps1…'
    Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
}

Write-Host 'Installing the latest .NET 10 SDK into .dotnet…'
& pwsh -NoProfile -ExecutionPolicy Bypass -File $installer `
    -Channel '10.0' `
    -Quality 'GA' `
    -InstallDir $install `
    -NoPath

& (Join-Path $install 'dotnet.exe') --info
