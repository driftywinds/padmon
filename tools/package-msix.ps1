[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PublishDirectory,

    [Parameter(Mandatory)]
    [string] $OutputDirectory,

    [Parameter(Mandatory)]
    [string] $Version,

    [string] $IdentityName = "driftywinds.padmon",

    [string] $Publisher = "CN=padmon-preview",

    [string] $PublisherDisplayName = "driftywinds",

    [string] $MakeAppxPath,

    [string] $CertificatePath,

    [string] $CertificatePassword
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$output = [System.IO.Path]::GetFullPath(
    $(if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
        $OutputDirectory
    }
    else {
        Join-Path $root $OutputDirectory
    }))

function Find-Tool {
    param(
        [string] $ExplicitPath,
        [string[]] $Patterns,
        [string] $DisplayName
    )

    if ($ExplicitPath) {
        $resolved = Get-Item -LiteralPath $ExplicitPath -ErrorAction SilentlyContinue
        if ($resolved) {
            return $resolved.FullName
        }

        throw "$DisplayName was not found at '$ExplicitPath'."
    }

    foreach ($pattern in $Patterns) {
        if ([string]::IsNullOrWhiteSpace($pattern)) {
            continue
        }

        $match = Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 1

        if ($match) {
            return $match.FullName
        }
    }

    throw "$DisplayName was not found. Install the Windows SDK or restore Microsoft.Windows.SDK.BuildTools."
}

$makeappx = Find-Tool `
    -ExplicitPath $MakeAppxPath `
    -DisplayName "MakeAppx.exe" `
    -Patterns @(
        (Get-Command makeappx.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source -ErrorAction SilentlyContinue),
        (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\*\x64\makeappx.exe"),
        (Join-Path $env:ProgramFiles "Windows Kits\10\bin\*\x64\makeappx.exe"),
        (Join-Path $root ".nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64\makeappx.exe")
    )

$exe = Join-Path $publish "Padmon.exe"
$pri = Join-Path $publish "resources.pri"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Missing Padmon.exe in '$publish'. Publish the app before packaging."
}
if (-not (Test-Path -LiteralPath $pri)) {
    throw "Missing resources.pri in '$publish'. Publish with the MSIX PRI properties before packaging."
}

$coreVersion = ($Version.Trim().TrimStart('v', 'V') -split '[-+]', 2)[0]
$parts = $coreVersion.Split('.')
if ($parts.Count -lt 3 -or $parts.Count -gt 4) {
    throw "Version '$Version' must have three or four numeric parts, for example 1.2.3 or 1.2.3.0."
}

$numbers = @()
foreach ($part in $parts) {
    $number = 0
    if (-not [int]::TryParse($part, [ref] $number) -or $number -lt 0 -or $number -gt 65535) {
        throw "Version part '$part' is not a number between 0 and 65535."
    }
    $numbers += $number
}
while ($numbers.Count -lt 4) {
    $numbers += 0
}

$packageVersion = $numbers -join '.'

if ($IdentityName -notmatch '^[A-Za-z0-9][A-Za-z0-9.-]{1,48}[A-Za-z0-9]$') {
    throw "IdentityName '$IdentityName' is not a valid package identity name."
}
if (-not $Publisher.StartsWith('CN=', [StringComparison]::OrdinalIgnoreCase)) {
    throw "Publisher must be the full certificate subject from Partner Center, starting with CN=."
}

if (Test-Path -LiteralPath $output) {
    Remove-Item -LiteralPath $output -Recurse -Force
}
New-Item -ItemType Directory -Path $output | Out-Null

$layout = Join-Path $output "layout"
$assets = Join-Path $layout "Assets"
New-Item -ItemType Directory -Path $layout, $assets -Force | Out-Null
Copy-Item -Path (Join-Path $publish "*") -Destination $layout -Recurse -Force

# The MSIX build produces resources.pri with the package identity as the
# resource map. Remove any stale unpackaged Padmon.pri copied by an earlier
# publish so the package has one unambiguous root resource index.
$packagedPri = Join-Path $layout "resources.pri"
$unpackagedPri = Join-Path $layout "Padmon.pri"
if (Test-Path -LiteralPath $packagedPri) {
    Remove-Item -LiteralPath $unpackagedPri -Force -ErrorAction SilentlyContinue
}

$packageAssets = Join-Path $root "src\Padmon.Package\Assets"
if (-not (Test-Path -LiteralPath $packageAssets)) {
    throw "Missing package artwork at '$packageAssets'."
}
Copy-Item -Path (Join-Path $packageAssets "*") -Destination $assets -Force

# Symbols belong in the .appxsym upload, not in the installed package.
Get-ChildItem -Path $layout -Recurse -Filter "*.pdb" -File | Remove-Item -Force

$manifestTemplate = Get-Content -Raw -LiteralPath (Join-Path $root "src\Padmon.Package\AppxManifest.xml")
$replacements = [ordered]@{
    "{{IDENTITY_NAME}}"       = $IdentityName
    "{{PUBLISHER}}"           = $Publisher
    "{{PUBLISHER_DISPLAY_NAME}}" = $PublisherDisplayName
    "{{PACKAGE_VERSION}}"     = $packageVersion
}

$manifest = $manifestTemplate
foreach ($entry in $replacements.GetEnumerator()) {
    $manifest = $manifest.Replace(
        $entry.Key,
        [System.Security.SecurityElement]::Escape([string] $entry.Value))
}

$manifestPath = Join-Path $layout "AppxManifest.xml"
[System.IO.File]::WriteAllText(
    $manifestPath,
    $manifest,
    [System.Text.UTF8Encoding]::new($false))

$baseName = "Padmon_${packageVersion}_x64"
$packagePath = Join-Path $output "$baseName.msix"

Write-Host "Packaging $packageVersion from $publish"
& $makeappx pack /o /d $layout /p $packagePath
if ($LASTEXITCODE -ne 0) {
    throw "MakeAppx failed with exit code $LASTEXITCODE."
}

if ($CertificatePath) {
    $signtool = Find-Tool `
        -DisplayName "SignTool.exe" `
        -Patterns @(
            (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin\*\x64\signtool.exe"),
            (Join-Path $env:ProgramFiles "Windows Kits\10\bin\*\x64\signtool.exe"),
            (Join-Path $root ".nuget\packages\microsoft.windows.sdk.buildtools\*\bin\*\x64\signtool.exe")
        )

    $signArguments = @("sign", "/fd", "SHA256", "/f", $CertificatePath)
    if ($CertificatePassword) {
        $signArguments += @("/p", $CertificatePassword)
    }
    $signArguments += @("/tr", "http://timestamp.digicert.com", "/td", "SHA256", $packagePath)

    & $signtool @signArguments
    if ($LASTEXITCODE -ne 0) {
        throw "SignTool failed with exit code $LASTEXITCODE."
    }
}

# Partner Center recommends a .msixupload. It is a zip containing the package
# (and optional .appxsym symbols), not a re-packaged MSIX.
$symbolsPath = Join-Path $output "$baseName.appxsym"
$symbols = Join-Path $output "symbols"
$pdbFiles = @(Get-ChildItem -Path $publish -Recurse -Filter "*.pdb" -File)
if ($pdbFiles.Count -gt 0) {
    New-Item -ItemType Directory -Path $symbols -Force | Out-Null
    foreach ($pdb in $pdbFiles) {
        Copy-Item -LiteralPath $pdb.FullName -Destination (Join-Path $symbols $pdb.Name) -Force
    }
    Compress-Archive -Path (Join-Path $symbols "*") -DestinationPath $symbolsPath -Force
}

$upload = Join-Path $output "$baseName.msixupload"
$uploadStage = Join-Path $output "upload"
New-Item -ItemType Directory -Path $uploadStage -Force | Out-Null
Copy-Item -LiteralPath $packagePath -Destination $uploadStage
if (Test-Path -LiteralPath $symbolsPath) {
    Copy-Item -LiteralPath $symbolsPath -Destination $uploadStage
}
Compress-Archive -Path (Join-Path $uploadStage "*") -DestinationPath $upload -Force

Remove-Item -LiteralPath $layout -Recurse -Force
Remove-Item -LiteralPath $uploadStage -Recurse -Force
if (Test-Path -LiteralPath $symbols) {
    Remove-Item -LiteralPath $symbols -Recurse -Force
}

Get-Item -LiteralPath $packagePath, $upload | Select-Object Name, Length
