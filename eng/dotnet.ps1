[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $Arguments
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root '.dotnet\dotnet.exe'

if (-not (Test-Path $dotnet)) {
    throw "The local .NET SDK is missing. Run tools\install-dotnet.ps1 first."
}

$env:DOTNET_ROOT = Join-Path $root '.dotnet'
$env:DOTNET_CLI_HOME = Join-Path $root '.dotnet-home'
$env:NUGET_PACKAGES = Join-Path $root '.nuget\packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $root '.nuget\http-cache'
$env:TEMP = Join-Path $root '.tmp'
$env:TMP = $env:TEMP
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
# The sandboxed SDK install does not include the workload locator packs, and
# padmon does not use workloads. Skip that resolver during builds.
$env:MSBuildEnableWorkloadResolver = 'false'

# NuGet probes %APPDATA%\NuGet even when --configfile is supplied. Point it at
# a repo-local profile so builds work inside the sandbox and never touch the
# user's real NuGet configuration.
$env:APPDATA = Join-Path $root '.appdata'
$fakeNuGet = Join-Path $env:APPDATA 'NuGet'
New-Item -ItemType Directory -Force $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:NUGET_HTTP_CACHE_PATH, $env:TEMP, $fakeNuGet | Out-Null
Copy-Item (Join-Path $root 'NuGet.Config') (Join-Path $fakeNuGet 'NuGet.Config') -Force

& $dotnet @Arguments
exit $LASTEXITCODE
