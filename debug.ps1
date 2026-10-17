<#
.SYNOPSIS
    Build KeyPlus in Debug, deploy it to the phone or to an emulator and open it.
.DESCRIPTION
    -Phone deploys to the physical phone attached over adb, by cable or by
    Wi-Fi: where none is attached it connects to the one Wireless debugging
    announces (adb mdns), which must have been paired once with adb pair.
    -Emulator deploys to a running emulator, or starts one: -Avd names it,
    otherwise you pick one from the installed AVDs. With neither, it asks.

    It deploys with `dotnet build -t:Install`, the one correct way for a
    Debug build: Debug APKs fast-deploy their assemblies apart from the APK,
    so an `adb install` of the APK by hand would run stale code. It builds
    for the device's own architecture (arm64 on the phone, x86_64 on most
    emulators). The Release APK stays build-apk.ps1's (rules.md); this one
    raises no build number.

    -Product says which KeyPlus: Popup, the window above the keyboard,
    whose service it turns on again after installing (installing turns it
    off), or Keyboard, KeyPlus as the keyboard, turned on and made the one
    in use. Without it a headset (Galaxy XR) gets the keyboard, and any
    other device asks. Then it opens the app. The first time on a device, turn
    it on by hand once, as README.md says, so Android has allowed it.
.EXAMPLE
    .\debug.ps1 -Phone
.EXAMPLE
    .\debug.ps1 -Emulator -Avd pixel_7_-_api_35
.EXAMPLE
    .\debug.ps1 -Phone -NoBuild
#>
[CmdletBinding()]
param(
    [switch]$Phone,
    [switch]$Emulator,
    [string]$Avd,
    # A device by its adb serial, when more than one of the kind is attached.
    [string]$Device,
    [ValidateSet("Popup", "Keyboard")]
    [string]$Product,
    [switch]$NoBuild,
    [switch]$Quiet
)

# Verbose unless asked not to: every command and dotnet's whole log are what
# a build is read by.
if (-not $Quiet) { $VerbosePreference = "Continue" }

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Csproj = Join-Path $Root "Berpiztu.KeyPlus.csproj"
$Tfm = "net10.0-android"
. (Join-Path $Root "adb.ps1")

if ($Phone -and $Emulator) { throw "Choose one: -Phone or -Emulator." }
if (-not $Phone -and -not $Emulator) {
    Write-Host "Debug KeyPlus on:" -ForegroundColor Cyan
    Write-Host "  [1] the phone"
    Write-Host "  [2] an emulator"
    do { $answer = Read-Host "Which? [1-2]" } until ($answer -in @("1", "2"))
    $Phone = $answer -eq "1"
    $Emulator = $answer -eq "2"
}

$emulatorExe = Join-Path $AndroidSdk "emulator\emulator.exe"

if ($Phone) {
    $target = Get-Phone $Device
} else {
    $emulators = @(Get-AttachedDevices | Where-Object { $_ -like "emulator-*" })
    if ($emulators.Count -eq 0) {
        if (-not $Avd) {
            $avds = @(& $emulatorExe -list-avds | Where-Object { $_ -and $_.Trim() -ne "" } | ForEach-Object { $_.Trim() })
            if ($avds.Count -eq 0) { throw "No emulator running and no AVD defined. Create one in Android Studio." }
            if ($avds.Count -eq 1) {
                $Avd = $avds[0]
            } else {
                Write-Host "Available emulators:" -ForegroundColor Cyan
                for ($i = 0; $i -lt $avds.Count; $i++) { Write-Host ("  [{0}] {1}" -f ($i + 1), $avds[$i]) }
                do {
                    $answer = Read-Host "Start which one? [1-$($avds.Count)]"
                    $index = 0
                    $valid = [int]::TryParse($answer, [ref]$index) -and $index -ge 1 -and $index -le $avds.Count
                } until ($valid)
                $Avd = $avds[$index - 1]
            }
        }
        Write-Host "Starting emulator '$Avd'..." -ForegroundColor Cyan
        Start-Process -FilePath $emulatorExe -ArgumentList @("-avd", $Avd, "-no-boot-anim") -WindowStyle Minimized | Out-Null
        $booted = ""
        for ($i = 0; $i -lt 120; $i++) {
            $emulators = @(Get-AttachedDevices | Where-Object { $_ -like "emulator-*" })
            if ($emulators.Count -gt 0) {
                $booted = (Invoke-Adb @("-s", $emulators[0], "shell", "getprop", "sys.boot_completed")) -join ""
                if ($booted.Trim() -eq "1") { break }
            }
            Start-Sleep -Seconds 2
        }
        if ($booted.Trim() -ne "1") { throw "The emulator did not finish booting." }
    }
    $target = Select-One $emulators "emulator" $Device
}

$env:ANDROID_SERIAL = $target
$Product = Select-Product $target $Product
Write-Host "Target device: $target (KeyPlus $Product)" -ForegroundColor DarkGray

# The device's own architecture, which the build is made for.
$abi = ((Invoke-Adb @("-s", $target, "shell", "getprop", "ro.product.cpu.abi")) -join "").Trim()
$rid = switch ($abi) {
    "arm64-v8a" { "android-arm64" }
    "x86_64" { "android-x64" }
    default { throw "KeyPlus is not built for the '$abi' architecture of $target." }
}

if (-not $NoBuild) {
    Write-Host "Building and deploying KeyPlus (Debug, $rid, fast deployment)..." -ForegroundColor Cyan
    $verbosity = if ($Quiet) { "minimal" } else { "normal" }
    Write-Verbose "dotnet build $Csproj -c Debug -f $Tfm -t:Install -p:KeyPlusProduct=$Product -p:RuntimeIdentifiers=$rid -p:AdbTarget=-s $target -v:$verbosity"
    dotnet build $Csproj -c Debug -f $Tfm -t:Install -nologo -v:$verbosity -p:KeyPlusProduct=$Product -p:RuntimeIdentifiers=$rid -p:AdbTarget="-s $target"
    if ($LASTEXITCODE -ne 0) { throw "dotnet build -t:Install failed with exit code $LASTEXITCODE" }
}

Enable-KeyPlus $target $Product
Open-KeyPlus $target $Product
