<#
.SYNOPSIS
    Build KeyPlus's two products' APKs, to copy to a device and install there.
.DESCRIPTION
    Raises the build number in version.props by one (the version is
    1.0.0.<build>, rules.md), builds the Release APKs (signed with the
    machine's Android debug key) and copies them to -OutputDirectory (dist by
    default): KeyPlus-Popup-<version>.apk, the keys in a window above the
    keyboard in use, and Berpiztu-AI-Keyboard-<version>.apk, the keys as
    the keyboard, for any Android and for a headset (Galaxy XR). Each carries
    everything it runs, so it installs on its own, with no computer: copy it
    to the device, open it to install it, then turn it on in the device's
    Settings (README.md). -Product builds only one of them. Commit version.props with the build.
.EXAMPLE
    .\build-apk.ps1
.EXAMPLE
    .\build-apk.ps1 -Product Keyboard -OutputDirectory D:\Shared
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "dist"),
    # Popup, Keyboard, or both.
    [ValidateSet("Both", "Popup", "Keyboard")]
    [string]$Product = "Both"
)

$ErrorActionPreference = "Stop"

# Each product's APK name, before its version.
function Get-ApkName {
    param([string]$Product)
    if ($Product -eq "Keyboard") { "Berpiztu-AI-Keyboard" } else { "KeyPlus-Popup" }
}
$project = Join-Path $PSScriptRoot "Berpiztu.KeyPlus.csproj"
$versionFile = Join-Path $PSScriptRoot "version.props"

$versionText = Get-Content $versionFile -Raw
$buildPattern = "(?<=<BuildNumber>)\d+(?=</BuildNumber>)"
$build = [int]([regex]::Match($versionText, $buildPattern).Value) + 1
Set-Content $versionFile ([regex]::Replace($versionText, $buildPattern, "$build")) -NoNewline
$version = "1.0.0.$build"

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$products = if ($Product -eq "Both") { @("Popup", "Keyboard") } else { @($Product) }
foreach ($keyPlusProduct in $products) {
    dotnet publish $project -c Release -nologo "-p:KeyPlusProduct=$keyPlusProduct"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($keyPlusProduct) failed with exit code $LASTEXITCODE" }

    $publish = Join-Path $PSScriptRoot "bin\$keyPlusProduct\Release\net10.0-android\publish"
    $apk = Get-ChildItem $publish -Filter "*-Signed.apk" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $apk) { throw "The $keyPlusProduct build left no signed APK in $publish." }

    $copy = Join-Path $OutputDirectory "$(Get-ApkName $keyPlusProduct)-$version.apk"
    Copy-Item $apk.FullName $copy -Force
    Write-Host "APK ready: $copy"
}
