param(
    [ValidateSet('SelfTest', 'List', 'Watch')]
    [string] $Mode = 'SelfTest',
    [string] $Controller,
    [switch] $Inject,
    [int] $Seconds = 30
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dotnet = Join-Path $root 'eng\dotnet.ps1'
$project = Join-Path $PSScriptRoot 'CouchHarness\CouchHarness.csproj'
$exe = Join-Path $PSScriptRoot 'CouchHarness\bin\x64\Release\net10.0\CouchHarness.exe'

& $dotnet build $project `
    --configuration Release `
    --property:Platform=x64 `
    --property:UseSharedCompilation=false `
    --property:BuildInParallel=false
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

switch ($Mode) {
    'SelfTest' {
        & $exe --self-test
    }

    'List' {
        & $exe --list
    }

    'Watch' {
        $watchArguments = @('--seconds', "$Seconds")
        if ($Controller) {
            $watchArguments += @('--controller', $Controller)
        }
        if ($Inject) {
            $watchArguments += '--inject'
        }

        & $exe @watchArguments
    }
}

exit $LASTEXITCODE
