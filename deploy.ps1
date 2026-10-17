<#
.SYNOPSIS
    Build the Release APK and install it on the phone or headset, by cable or by Wi-Fi.
.DESCRIPTION
    Finds the phone or headset as debug.ps1 finds it, on the cable or by
    Wireless debugging (paired once with adb pair), and runs build-apk.ps1
    for the product (which raises the build number in version.props):
    -Product Popup, dist\KeyPlus-Popup-1.0.0.<build>.apk, or Keyboard,
    dist\Berpiztu-AI-Keyboard-1.0.0.<build>.apk; without it a headset (Galaxy
    XR) gets the keyboard, and any other device asks. It installs that APK
    over the one on the device with adb, turns it on (installing turns the
    popup's service off; the keyboard is made the one in use) and opens the
    app. With -NoBuild it installs the newest APK of the product already in
    dist.

    A Release APK carries everything in it, so installing it with adb is
    right; a Debug build is debug.ps1's.
.EXAMPLE
    .\deploy.ps1
.EXAMPLE
    .\deploy.ps1 -NoBuild
#>
[CmdletBinding()]
param(
    # The phone by its adb serial, when more than one is attached.
    [string]$Device,
    [ValidateSet("Popup", "Keyboard")]
    [string]$Product,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $Root "adb.ps1")

$target = Get-Phone $Device
$Product = Select-Product $target $Product
if (-not $NoBuild) {
    & (Join-Path $Root "build-apk.ps1") -Product $Product
}

# The newest APK build-apk.ps1 left for the product: the highest build number.
$apk = Get-ChildItem (Join-Path $Root "dist") -Filter "$(if ($Product -eq "Keyboard") { "Berpiztu-AI-Keyboard" } else { "KeyPlus-Popup" })-*.apk" -ErrorAction SilentlyContinue |
    Sort-Object { [int](($_.BaseName -split "\.")[-1]) } -Descending |
    Select-Object -First 1
if (-not $apk) { throw "No $Product APK in dist: run it without -NoBuild, or build-apk.ps1." }

Write-Host "Installing $($apk.Name) on $target..." -ForegroundColor Cyan
$answer = (Invoke-Adb @("-s", $target, "install", "-r", $apk.FullName)) -join " "
if ($answer -notmatch "Success") { throw "adb install failed: $answer" }

Enable-KeyPlus $target $Product
Open-KeyPlus $target $Product
