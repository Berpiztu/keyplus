<#
.SYNOPSIS
    Build Berpiztu AI Keyboard's Windows installer:
    dist\Berpiztu-AI-Keyboard-Windows.msi.
.DESCRIPTION
    Publishes the Windows app (windows\, self-contained for x64, so no .NET
    is installed first) into dist\windows-payload at the release's version,
    the Android apps' (Berpiztu.KeyPlus.csproj's ApplicationDisplayVersion),
    and builds the per-user MSI around it (packaging\windows-install, WiX 5,
    as wslc-ai-agent's client installer). The release workflow runs it on a
    tag, beside the two APKs.
.PARAMETER Version
    The installer's version, x.y.z; the csproj's when none is given.
.EXAMPLE
    .\build-windows-installer.ps1
#>
param(
    [string]$Version
)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

if (-not $Version) {
    $Version = ([xml](Get-Content (Join-Path $root "Berpiztu.KeyPlus.csproj"))).Project.PropertyGroup.ApplicationDisplayVersion |
        Where-Object { $_ -is [string] } | Select-Object -First 1
}

$dist = Join-Path $root "dist"
$payload = Join-Path $dist "windows-payload"
if (Test-Path $payload) { Remove-Item -Recurse -Force $payload }
New-Item -ItemType Directory -Force -Path $payload | Out-Null

Write-Host "Publishing Berpiztu AI Keyboard for Windows $Version..."
& dotnet publish (Join-Path $root "windows\Berpiztu.KeyPlus.Windows.csproj") -c Release -r win-x64 --self-contained true `
    -p:Version=$Version -p:PublishSingleFile=false -o $payload
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
foreach ($needed in "BerpiztuKeyboard.exe", "keyplus_swipe.dll", "strings\values\strings.xml") {
    if (-not (Test-Path (Join-Path $payload $needed))) { throw "The payload has no $needed." }
}

# The licence the installer's first page shows: LICENSE, as RTF.
$license = Join-Path $dist "windows-license.rtf"
$text = (Get-Content (Join-Path $root "LICENSE") -Raw) -replace '\\', '\\' -replace '\{', '\{' -replace '\}', '\}'
$paragraphs = ($text -split "\r?\n") -join "\par`n"
Set-Content -Path $license -Encoding ascii -Value "{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\f0\fs18 $paragraphs}"

Write-Host "Building the installer..."
$wixproj = Join-Path $root "packaging\windows-install\BerpiztuKeyboard.Windows.wixproj"
& dotnet build $wixproj -c Release -p:MsiVersion=$Version -p:Payload=$payload -p:LicenseRtf=$license -o $dist
if ($LASTEXITCODE -ne 0) { throw "The installer's build failed with exit code $LASTEXITCODE" }

$msi = Join-Path $dist "Berpiztu-AI-Keyboard-Windows.msi"
if (-not (Test-Path $msi)) { throw "No $msi was made." }
Write-Host "Built: $msi ($([math]::Round((Get-Item $msi).Length / 1MB, 1)) MB)"
