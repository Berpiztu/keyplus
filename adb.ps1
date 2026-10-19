<#
.SYNOPSIS
    What debug.ps1 and deploy.ps1 share: adb, the phone, KeyPlus's service.
.DESCRIPTION
    Dot-sourced, not run: finds adb, finds the phone (by cable or by Wi-Fi),
    turns the KeyPlus keys service on again after an install, and opens the
    app, for either product: the popup ($PopupPackageId) or the keyboard
    ($KeyboardPackageId).
#>

$PopupPackageId = "ai.berpiztu.keyplus"
$KeyboardPackageId = "ai.berpiztu.keyboard"

# The keyboard's service, as Android names it: its package and its class.
$KeyboardId = "$KeyboardPackageId/.KeyPlusKeyboard"

function Get-PackageId {
    param([string]$Product)
    if ($Product -eq "Keyboard") { $KeyboardPackageId } else { $PopupPackageId }
}

# The Android SDK the android workload uses: ANDROID_HOME, ANDROID_SDK_ROOT,
# then where Visual Studio and Android Studio install it.
$AndroidSdk = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, "${env:ProgramFiles(x86)}\Android\android-sdk", "$env:LOCALAPPDATA\Android\Sdk") |
    Where-Object { $_ -and (Test-Path -LiteralPath (Join-Path $_ "platform-tools\adb.exe")) } |
    Select-Object -First 1
if (-not $AndroidSdk) { throw "Android SDK not found. Set ANDROID_HOME to the folder that contains platform-tools\adb.exe." }
$Adb = Join-Path $AndroidSdk "platform-tools\adb.exe"

# Runs adb and returns its output, its standard error dropped: Windows
# PowerShell turns each line a native command writes there into an error,
# which under Stop ends the script, and adb writes its notices there.
function Invoke-Adb {
    param([string[]]$Arguments)
    $previous = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $Adb @Arguments 2>$null
    } finally {
        $ErrorActionPreference = $previous
    }
}

function Get-AttachedDevices {
    Invoke-Adb @("devices") | Select-Object -Skip 1 | Where-Object { $_ -match "^\S+\s+device$" } | ForEach-Object { ($_ -split "\s+")[0] }
}

# A device's model, as its settings name it: SM-F946B, SM-I610.
function Get-DeviceModel {
    param([string]$Serial)
    return ((Invoke-Adb @("-s", $Serial, "shell", "getprop", "ro.product.model")) -join "").Trim()
}

# One of the devices attached of a kind: the one named, the only one, or,
# where there are several, the one chosen from a list with their models.
function Select-One {
    param([string[]]$Candidates, [string]$Kind, [string]$Named)
    if ($Named) {
        if ($Candidates -notcontains $Named) { throw "$Kind '$Named' is not attached. Attached: $($Candidates -join ', ')" }
        return $Named
    }
    if ($Candidates.Count -eq 1) {
        return $Candidates[0]
    }

    Write-Host "More than one $Kind attached:" -ForegroundColor Cyan
    for ($i = 0; $i -lt $Candidates.Count; $i++) {
        Write-Host ("  [{0}] {1}  ({2})" -f ($i + 1), (Get-DeviceModel $Candidates[$i]), $Candidates[$i])
    }
    do {
        $answer = Read-Host "Which one? [1-$($Candidates.Count)]"
        $index = 0
        $valid = [int]::TryParse($answer, [ref]$index) -and $index -ge 1 -and $index -le $Candidates.Count
    } until ($valid)
    return $Candidates[$index - 1]
}

# The phone (or headset) attached over adb, by cable or by Wi-Fi: every
# device Wireless debugging announces (adb mdns), paired once with adb pair,
# is connected first, so one asleep a moment ago is listed too. adb finds them
# a few seconds after it starts looking, so where none is attached it is
# asked again for a while before giving up.
function Get-Phone {
    param([string]$Named)
    $tried = @()
    $announced = @()
    for ($i = 0; $i -lt 10; $i++) {
        if ($i -gt 0) { Start-Sleep -Seconds 1 }
        $announced = @(Invoke-Adb @("mdns", "services") | Where-Object { $_ -match "_adb-tls-connect\._tcp" } |
            ForEach-Object { if ($_ -match "(\d{1,3}(\.\d{1,3}){3}:\d+)") { $Matches[1] } })
        if ($announced.Count -gt 0 -or @(Get-AttachedDevices | Where-Object { $_ -notlike "emulator-*" }).Count -gt 0) { break }
    }
    foreach ($address in $announced) {
        $answer = ((Invoke-Adb @("connect", $address)) -join " ").Trim()
        $tried += "$address ($answer)"
    }

    $phones = @(Get-AttachedDevices | Where-Object { $_ -notlike "emulator-*" })
    if ($phones.Count -eq 0) {
        $seen = if ($tried.Count -gt 0) { "Tried over Wi-Fi: $($tried -join '; '). " } else { "No phone announces Wireless debugging on this network. " }
        throw "No phone attached. ${seen}Plug it in with USB debugging on, or on the phone turn Wireless debugging on (Developer options); if it still does not connect, pair it again there with 'Pair device with pairing code' and: .\enable.ps1 -Pair <ip>:<port> -Code <code>"
    }
    return Select-One $phones "phone" $Named
}

# Whether KeyPlus's service is on: bound, and not marked crashed (killed by an
# install), which opens no window. Listed by its package on some phones, by
# its label alone on others (the emulator).
function Test-ServiceBound {
    param([string]$Target)
    $state = Invoke-Adb @("-s", $Target, "shell", "dumpsys", "accessibility")
    $bound = $state | Where-Object { $_ -match "Bound services:" } | Select-Object -First 1
    $crashed = $state | Where-Object { $_ -match "Crashed services:" } | Select-Object -First 1
    return [bool]($bound -match "$([regex]::Escape($PopupPackageId))|label=KeyPlus keys") -and -not ($crashed -match [regex]::Escape($PopupPackageId))
}

# The service on again where installing turned it off: Android stops the app,
# and its accessibility service with it. Left alone while it is still bound;
# else taken out of the list, the app stopped (a service marked crashed is not
# bound again while its process lives on), opened again and the service put
# back; and where it still is not on, said, with the commands to do it by
# hand, for then only those or the switch in Settings bring it back.
function Enable-KeyPlusService {
    param([string]$Target)

    # Allowed restricted settings first, as Settings does from the app's info:
    # Android 13 and later keep an app installed outside a store from being
    # an accessibility service until they are.
    Invoke-Adb @("-s", $Target, "shell", "appops", "set", $PopupPackageId, "ACCESS_RESTRICTED_SETTINGS", "allow") | Out-Null

    # The secure settings granted too, so the app's own "Turn on KeyPlus keys"
    # turns the service on again later without a computer.
    Invoke-Adb @("-s", $Target, "shell", "pm", "grant", $PopupPackageId, "android.permission.WRITE_SECURE_SETTINGS") | Out-Null
    if (Test-ServiceBound $Target) {
        return
    }

    $service = "$PopupPackageId/$PopupPackageId.KeyPlusService"
    $listed = ((Invoke-Adb @("-s", $Target, "shell", "settings", "get", "secure", "enabled_accessibility_services")) -join "").Trim()
    $others = @($listed -split ":" | Where-Object { $_ -and $_ -ne "null" -and $_ -notlike "$PopupPackageId/*" })
    Invoke-Adb @("-s", $Target, "shell", "settings", "put", "secure", "enabled_accessibility_services", $(if ($others.Count -gt 0) { $others -join ":" } else { '""' })) | Out-Null
    Invoke-Adb @("-s", $Target, "shell", "am", "force-stop", $PopupPackageId) | Out-Null

    # The app opened before its service is put back: INAIR's pod binds no
    # service of an app that is not running ("binder service error").
    Invoke-Adb @("-s", $Target, "shell", "monkey", "-p", $PopupPackageId, "-c", "android.intent.category.LAUNCHER", "1") | Out-Null
    Start-Sleep -Seconds 2
    Invoke-Adb @("-s", $Target, "shell", "settings", "put", "secure", "enabled_accessibility_services", ((@($others) + $service) -join ":")) | Out-Null
    Invoke-Adb @("-s", $Target, "shell", "settings", "put", "secure", "accessibility_enabled", "1") | Out-Null
    $bound = $false
    for ($i = 0; $i -lt 5 -and -not $bound; $i++) {
        Start-Sleep -Seconds 1
        $bound = Test-ServiceBound $Target
    }
    if ($bound) {
        Write-Host "KeyPlus keys turned on again in $Target's accessibility settings" -ForegroundColor DarkGray
    } else {
        Write-Host "KeyPlus keys is off on ${Target}: turn it off and on in Settings > Accessibility > Installed apps > KeyPlus keys (allow restricted settings in its app info first if Android asks), or by hand, keeping the services listed before it:" -ForegroundColor Yellow
        Write-Host "  adb -s $Target shell appops set $PopupPackageId ACCESS_RESTRICTED_SETTINGS allow"
        Write-Host "  adb -s $Target shell monkey -p $PopupPackageId -c android.intent.category.LAUNCHER 1"
        Write-Host "  adb -s $Target shell settings put secure enabled_accessibility_services `"$((@($others) + $service) -join ':')`""
        Write-Host "  adb -s $Target shell settings put secure accessibility_enabled 1"
    }
}

# Whether the device is a headset (Android XR, the Galaxy XR), which keeps
# the popup before the user's eyes: there it gets Berpiztu AI Keyboard.
function Test-Headset {
    param([string]$Target)
    return ((Invoke-Adb @("-s", $Target, "shell", "pm", "has-feature", "android.software.xr.api.spatial")) -join "").Trim() -eq "true"
}

# Berpiztu AI Keyboard turned on in the device's keyboard list and made the
# one in use. A keyboard just installed reaches Android's list a moment later
# (INAIR's pod): asked again, a second apart, until it is the one in use; if
# it never is, the commands to do it by hand are said. ⚙'s Cambiar de teclado
# goes back to another keyboard.
function Enable-KeyPlusKeyboard {
    param([string]$Target)
    $current = ""
    for ($i = 0; $i -lt 5; $i++) {
        Invoke-Adb @("-s", $Target, "shell", "ime", "enable", $KeyboardId) | Out-Null
        Invoke-Adb @("-s", $Target, "shell", "ime", "set", $KeyboardId) | Out-Null
        $current = ((Invoke-Adb @("-s", $Target, "shell", "settings", "get", "secure", "default_input_method")) -join "").Trim()
        if ($current -like "$KeyboardPackageId/*") {
            Write-Host "Berpiztu AI Keyboard is the keyboard in use on $Target" -ForegroundColor DarkGray
            return
        }

        Start-Sleep -Seconds 1
    }

    throw "Berpiztu AI Keyboard could not be made the keyboard in use on ${Target}: it is $current. By hand:`n  adb -s $Target shell ime enable $KeyboardId`n  adb -s $Target shell ime set $KeyboardId"
}

# The product to install on a device: the one named; on a headset the
# keyboard, for the popup cannot follow a keyboard there; else the one chosen.
function Select-Product {
    param([string]$Target, [string]$Named)
    if ($Named) { return $Named }
    if (Test-Headset $Target) {
        Write-Host "$(Get-DeviceModel $Target) is a headset: Berpiztu AI Keyboard" -ForegroundColor DarkGray
        return "Keyboard"
    }

    Write-Host "Which KeyPlus for $(Get-DeviceModel $Target)?" -ForegroundColor Cyan
    Write-Host "  [1] Popup: the keys above the keyboard you use"
    Write-Host "  [2] Keyboard: Berpiztu AI Keyboard, the keys as the keyboard"
    do { $answer = Read-Host "Which? [1-2]" } until ($answer -in @("1", "2"))
    return $(if ($answer -eq "2") { "Keyboard" } else { "Popup" })
}

# A product turned on after installing it: the popup's service, or the keyboard.
function Enable-KeyPlus {
    param([string]$Target, [string]$Product)
    if ($Product -eq "Keyboard") { Enable-KeyPlusKeyboard $Target } else { Enable-KeyPlusService $Target }
}

function Open-KeyPlus {
    param([string]$Target, [string]$Product)
    $packageId = Get-PackageId $Product
    Write-Host "Opening $packageId on $Target" -ForegroundColor Green
    Invoke-Adb @("-s", $Target, "shell", "monkey", "-p", $packageId, "-c", "android.intent.category.LAUNCHER", "1") | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "adb could not open $packageId on $Target" }
    Write-Host "Logs: adb -s $Target logcat -s KeyPlus DOTNET" -ForegroundColor DarkGray
}
