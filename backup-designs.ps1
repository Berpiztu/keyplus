<#
.SYNOPSIS
    Keep a copy of every keyboard design on a device that changed since its last copy.
.DESCRIPTION
    Reads by adb each screen's design of Berpiztu AI Keyboard (the preference
    abcDesign in the app's keyplus.<screen>.xml, through run-as, so a Debug
    build) on the device, and writes it to docs\tODO\designs as a new file,
    <date>_<time>_<device model>_<screen>.txt, when it differs from the
    newest copy of that device and screen. A copy is never written over:
    every change stays. Run it before installing, before touching an
    emulator and before every commit.
.EXAMPLE
    .\backup-designs.ps1
.EXAMPLE
    .\backup-designs.ps1 -Device emulator-5554
#>
[CmdletBinding()]
param(
    # A device by its adb serial; without it, every device attached.
    [string]$Device
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $Root "adb.ps1")
$Folder = Join-Path $Root "docs\tODO\designs"
New-Item -ItemType Directory -Force -Path $Folder | Out-Null

$devices = if ($Device) { @($Device) } else { @(Get-AttachedDevices) }
if ($devices.Count -eq 0) {
    Write-Host "No device attached: nothing to copy." -ForegroundColor Yellow
    return
}

foreach ($serial in $devices) {
    $model = ((Invoke-Adb @("-s", $serial, "shell", "getprop", "ro.product.model")) -join "").Trim() -replace '[^A-Za-z0-9_-]', '-'
    $files = @(Invoke-Adb @("-s", $serial, "shell", "run-as", $KeyboardPackageId, "ls", "shared_prefs") |
        ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^keyplus\..+\.xml$' })
    if ($files.Count -eq 0) {
        Write-Host "${model}: no designs readable (not installed, or not a Debug build)." -ForegroundColor Yellow
        continue
    }

    foreach ($file in $files) {
        $xml = (Invoke-Adb @("-s", $serial, "exec-out", "run-as", $KeyboardPackageId, "cat", "shared_prefs/$file")) -join "`n"
        if ($xml -notmatch '<string name="abcDesign">([^<]*)</string>') {
            continue
        }

        $design = $Matches[1]
        $screen = ($file -replace '^keyplus\.', '' -replace '\.xml$', '')
        $newest = Get-ChildItem -LiteralPath $Folder -Filter "*_${model}_${screen}.txt" -ErrorAction SilentlyContinue |
            Sort-Object Name | Select-Object -Last 1
        if ($newest -and ((Get-Content -LiteralPath $newest.FullName -Raw).Trim() -eq $design)) {
            Write-Host "${model} ${screen}: unchanged since $($newest.Name)." -ForegroundColor DarkGray
            continue
        }

        $name = "{0}_{1}_{2}.txt" -f (Get-Date -Format "yyyy-MM-dd_HHmmss"), $model, $screen
        Set-Content -LiteralPath (Join-Path $Folder $name) -Value $design -NoNewline -Encoding utf8NoBOM
        Write-Host "${model} ${screen}: copied to docs\tODO\designs\$name" -ForegroundColor Green
    }
}
