<#
.SYNOPSIS
    Build KeyPlus and install it on the Android phone attached over adb.
.DESCRIPTION
    Builds the Release APK (signed with the machine's Android debug key) and
    installs it on the one device adb sees, or on -Device when it sees more.
    Then turn the service on in the phone's Settings: see README.md.
.EXAMPLE
    .\install.ps1
.EXAMPLE
    .\install.ps1 -Device R58M123ABC -NoBuild
#>
[CmdletBinding()]
param(
    [string]$Device,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "Berpiztu.KeyPlus.csproj"

if (-not $NoBuild) {
    dotnet publish $project -c Release -nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
}

$apk = Get-ChildItem (Join-Path $PSScriptRoot "bin\Release") -Recurse -Filter "com.berpiztu.keyplus-Signed.apk" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $apk) { throw "No signed APK under bin\Release: build without -NoBuild first." }

$adb = (Get-Command adb -ErrorAction SilentlyContinue)?.Source
if (-not $adb) {
    $adb = @(
        (Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Android\android-sdk\platform-tools\adb.exe")
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $adb) { throw "adb not found: install the Android SDK platform tools." }

$target = if ($Device) { @("-s", $Device) } else { @() }
& $adb @target install -r $apk.FullName
if ($LASTEXITCODE -ne 0) { throw "adb install failed with exit code $LASTEXITCODE" }
Write-Host "Installed $($apk.Name). On the phone: open KeyPlus and turn its service on."
