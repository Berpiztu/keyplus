<#
.SYNOPSIS
    Turn KeyPlus on on a phone or a headset, by adb, with nothing to find in its Settings.
.DESCRIPTION
    Pairs the device first where -Pair and -Code are given (Developer
    options > Wireless debugging > Pair device with pairing code, once per
    device), finds it by cable or by Wi-Fi, asking which where there are
    several, allows KeyPlus restricted settings and turns its KeyPlus keys
    service on, keeping any other accessibility service on as it was. Run it
    again after installing a new KeyPlus, which turns the service off.
    -Product Keyboard turns Berpiztu AI Keyboard on instead and makes it the
    one in use; without -Product a headset (Galaxy XR) gets the keyboard,
    and any other device asks.
.EXAMPLE
    .\enable.ps1
.EXAMPLE
    .\enable.ps1 -Pair 192.168.1.20:37000 -Code 123456
#>
[CmdletBinding()]
param(
    # The ip:port the device shows under "Pair device with pairing code".
    [string]$Pair,
    # The pairing code it shows there.
    [string]$Code,
    # The device by its adb serial, to skip the question.
    [string]$Device,
    [ValidateSet("Popup", "Keyboard")]
    [string]$Product
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $Root "adb.ps1")

if ($Pair) {
    if (-not $Code) { throw "Give the pairing code too: -Code <code>." }
    $answer = (Invoke-Adb @("pair", $Pair, $Code)) -join " "
    if ($answer -notmatch "Successfully paired") { throw "Pairing failed: $answer" }
    Write-Host "Paired with $Pair" -ForegroundColor DarkGray
}

$target = Get-Phone $Device
$Product = Select-Product $target $Product
$packageId = Get-PackageId $Product
if (-not ((Invoke-Adb @("-s", $target, "shell", "pm", "list", "packages", $packageId)) -match [regex]::Escape($packageId))) {
    throw "$packageId is not installed on $(Get-DeviceModel $target): install it first (.\deploy.ps1)."
}

Enable-KeyPlus $target $Product
if ($Product -eq "Keyboard" -or (Test-ServiceBound $target)) {
    Write-Host "KeyPlus is on on $(Get-DeviceModel $target)." -ForegroundColor Green
}
