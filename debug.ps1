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

    After installing it turns the KeyPlus keys service on again (installing
    turns it off) and opens the KeyPlus app. The first time on a device, turn
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
$PackageId = "com.berpiztu.keyplus"

if ($Phone -and $Emulator) { throw "Choose one: -Phone or -Emulator." }
if (-not $Phone -and -not $Emulator) {
    Write-Host "Debug KeyPlus on:" -ForegroundColor Cyan
    Write-Host "  [1] the phone"
    Write-Host "  [2] an emulator"
    do { $answer = Read-Host "Which? [1-2]" } until ($answer -in @("1", "2"))
    $Phone = $answer -eq "1"
    $Emulator = $answer -eq "2"
}

# The Android SDK the android workload uses: ANDROID_HOME, ANDROID_SDK_ROOT,
# then where Visual Studio and Android Studio install it.
$sdk = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, "${env:ProgramFiles(x86)}\Android\android-sdk", "$env:LOCALAPPDATA\Android\Sdk") |
    Where-Object { $_ -and (Test-Path -LiteralPath (Join-Path $_ "platform-tools\adb.exe")) } |
    Select-Object -First 1
if (-not $sdk) { throw "Android SDK not found. Set ANDROID_HOME to the folder that contains platform-tools\adb.exe." }
$adb = Join-Path $sdk "platform-tools\adb.exe"
$emulatorExe = Join-Path $sdk "emulator\emulator.exe"

# Runs adb and returns its output, its standard error dropped: Windows
# PowerShell turns each line a native command writes there into an error,
# which under Stop ends the script, and adb writes its notices there.
function Invoke-Adb {
    param([string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $adb @Arguments 2>$null
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Get-AttachedDevices {
    Invoke-Adb @("devices") | Select-Object -Skip 1 | Where-Object { $_ -match "^\S+\s+device$" } | ForEach-Object { ($_ -split "\s+")[0] }
}

function Select-One {
    param([string[]]$Candidates, [string]$Kind)
    if ($Device) {
        if ($Candidates -notcontains $Device) { throw "$Kind '$Device' is not attached. Attached: $($Candidates -join ', ')" }
        return $Device
    }
    if ($Candidates.Count -gt 1) { throw "More than one $Kind attached ($($Candidates -join ', ')): name one with -Device." }
    return $Candidates[0]
}

if ($Phone) {
    $phones = @(Get-AttachedDevices | Where-Object { $_ -notlike "emulator-*" })
    $tried = @()
    if ($phones.Count -eq 0) {
        # Not on the cable nor connected: the phone Wireless debugging
        # announces on the network, paired before, connected now. adb finds
        # it a few seconds after it starts looking, so it is asked again
        # for a while before giving up.
        $announced = @()
        for ($i = 0; $i -lt 10 -and $announced.Count -eq 0; $i++) {
            if ($i -gt 0) { Start-Sleep -Seconds 1 }
            $announced = @(Invoke-Adb @("mdns", "services") | Where-Object { $_ -match "_adb-tls-connect\._tcp" } |
                ForEach-Object { if ($_ -match "(\d{1,3}(\.\d{1,3}){3}:\d+)") { $Matches[1] } })
        }
        foreach ($address in $announced) {
            Write-Host "Connecting to $address over Wi-Fi..." -ForegroundColor DarkGray
            $answer = ((Invoke-Adb @("connect", $address)) -join " ").Trim()
            $tried += "$address ($answer)"
        }
        $phones = @(Get-AttachedDevices | Where-Object { $_ -notlike "emulator-*" })
    }
    if ($phones.Count -eq 0) {
        $seen = if ($tried.Count -gt 0) { "Tried over Wi-Fi: $($tried -join '; '). " } else { "No phone announces Wireless debugging on this network. " }
        throw "No phone attached. ${seen}Plug it in with USB debugging on, or on the phone turn Wireless debugging on (Developer options); if it still does not connect, pair it again there with 'Pair device with pairing code' and: adb pair <ip>:<port> <code>"
    }
    $target = Select-One $phones "phone"
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
    $target = Select-One $emulators "emulator"
}

$env:ANDROID_SERIAL = $target
Write-Host "Target device: $target" -ForegroundColor DarkGray

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
    Write-Verbose "dotnet build $Csproj -c Debug -f $Tfm -t:Install -p:RuntimeIdentifiers=$rid -p:AdbTarget=-s $target -v:$verbosity"
    dotnet build $Csproj -c Debug -f $Tfm -t:Install -nologo -v:$verbosity -p:RuntimeIdentifiers=$rid -p:AdbTarget="-s $target"
    if ($LASTEXITCODE -ne 0) { throw "dotnet build -t:Install failed with exit code $LASTEXITCODE" }
}

# The service on again where installing turned it off: Android stops the
# app, and its accessibility service with it. Left alone while it is still
# bound, for taking it out of the list unbinds it, and a Samsung phone does
# not bind it again from adb; taken out and put back where it is not bound,
# which brings it back on the emulator; and where it still is not, said, for
# then only the switch in Settings brings it back.
function Test-ServiceBound {
    $state = Invoke-Adb @("-s", $target, "shell", "dumpsys", "accessibility")
    $bound = $state | Where-Object { $_ -match "Bound services:" } | Select-Object -First 1
    $crashed = $state | Where-Object { $_ -match "Crashed services:" } | Select-Object -First 1
    # Listed by its package on some phones, by its label alone on others (the
    # emulator); bound but marked crashed (killed by the install) is not on:
    # it opens no window.
    return [bool]($bound -match "$([regex]::Escape($PackageId))|label=KeyPlus keys") -and -not ($crashed -match [regex]::Escape($PackageId))
}

if (-not (Test-ServiceBound)) {
    $service = "$PackageId/$PackageId.KeyPlusService"
    $listed = ((Invoke-Adb @("-s", $target, "shell", "settings", "get", "secure", "enabled_accessibility_services")) -join "").Trim()
    $others = @($listed -split ":" | Where-Object { $_ -and $_ -ne "null" -and $_ -notlike "$PackageId/*" })
    Invoke-Adb @("-s", $target, "shell", "settings", "put", "secure", "enabled_accessibility_services", $(if ($others.Count -gt 0) { $others -join ":" } else { '""' })) | Out-Null
    # A service Android marked crashed is not bound again while its process
    # lives on: the app stopped first, the service starts afresh.
    Invoke-Adb @("-s", $target, "shell", "am", "force-stop", $PackageId) | Out-Null
    Invoke-Adb @("-s", $target, "shell", "settings", "put", "secure", "enabled_accessibility_services", ((@($others) + $service) -join ":")) | Out-Null
    Invoke-Adb @("-s", $target, "shell", "settings", "put", "secure", "accessibility_enabled", "1") | Out-Null
    $bound = $false
    for ($i = 0; $i -lt 5 -and -not $bound; $i++) {
        Start-Sleep -Seconds 1
        $bound = Test-ServiceBound
    }
    if ($bound) {
        Write-Host "KeyPlus keys turned on again in $target's accessibility settings" -ForegroundColor DarkGray
    } else {
        Write-Host "KeyPlus keys is off on ${target}: turn it off and on in Settings > Accessibility > Installed apps > KeyPlus keys (allow restricted settings in its app info first if Android asks)." -ForegroundColor Yellow
    }
}

Write-Host "Opening KeyPlus on $target" -ForegroundColor Green
Invoke-Adb @("-s", $target, "shell", "monkey", "-p", $PackageId, "-c", "android.intent.category.LAUNCHER", "1") | Out-Null
if ($LASTEXITCODE -ne 0) { throw "adb could not open $PackageId on $target" }
Write-Host "Logs: adb -s $target logcat -s KeyPlus DOTNET" -ForegroundColor DarkGray
