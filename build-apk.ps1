<#
.SYNOPSIS
    Build KeyPlus's two products' APKs, to copy to a device and install there.
.DESCRIPTION
    Raises the build number in version.props by one (the version is
    1.0.0.<build>, rules.md), builds the Release APKs and copies them to
    -OutputDirectory (dist by default): KeyPlus-Popup-<version>.apk, the keys
    in a window above the keyboard in use, and
    Berpiztu-AI-Keyboard-<version>.apk, the keys as the keyboard, for any
    Android and for a headset (Galaxy XR). Each carries everything it runs,
    so it installs on its own, with no computer: copy it to the device, open
    it to install it, then turn it on in the device's Settings (README.md).
    -Product builds only one of them. Commit version.props with the build.

    Both are signed with the same key on every machine, here and on GitHub
    (.github/workflows/release.yml), for Android refuses to update an app
    from an APK signed with another key. The key is found as
    Resolve-KeyPlusSigning says, and made the first time there is none.
.PARAMETER NoBump
    Build at the version version.props holds, without raising it: a release
    built on GitHub from its tag (deploy-release.ps1 raised it already).
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
    [string]$Product = "Both",
    [switch]$NoBump
)

$ErrorActionPreference = "Stop"

# Each product's APK name, before its version.
function Get-ApkName {
    param([string]$Product)
    if ($Product -eq "Keyboard") { "Berpiztu-AI-Keyboard" } else { "KeyPlus-Popup" }
}

function Find-Keytool {
    $candidates = @()
    if ($env:JAVA_HOME) { $candidates += (Join-Path $env:JAVA_HOME "bin\keytool.exe") }
    $candidates += Get-ChildItem -Path "${env:ProgramFiles(x86)}\Android\openjdk", "$env:ProgramFiles\Microsoft\jdk-*", "$env:ProgramFiles\Android\jdk" -Filter keytool.exe -Recurse -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty FullName
    $command = Get-Command keytool -ErrorAction SilentlyContinue
    if ($command) { $candidates += $command.Source }
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate }
    }
    throw "keytool.exe not found. Install a JDK (the Android workload's OpenJDK is enough) or set JAVA_HOME."
}

# The key every APK is signed with, as wslc-ai-agent finds its own:
#   1. KEYPLUS_KEYSTORE (+ KEYPLUS_KEYSTORE_PASS, KEYPLUS_KEY_ALIAS, KEYPLUS_KEY_PASS), GitHub's way
#   2. private\android.keystore in this checkout, its password in android.keystore.pass beside it
#   3. %USERPROFILE%\.keyplus\android.keystore, the same pair, for one key for several checkouts
# With none of them, a new key is made at (2). Back it up: an APK signed with
# any other key cannot update the installed app.
function Resolve-KeyPlusSigning {
    $keystore = $env:KEYPLUS_KEYSTORE
    $storePass = $env:KEYPLUS_KEYSTORE_PASS
    $alias = if ($env:KEYPLUS_KEY_ALIAS) { $env:KEYPLUS_KEY_ALIAS } else { "keyplus" }
    $keyPass = $env:KEYPLUS_KEY_PASS

    if (-not $keystore) {
        $private = Join-Path $PSScriptRoot "private\android.keystore"
        $perUser = Join-Path $env:USERPROFILE ".keyplus\android.keystore"
        $keystore = if (-not (Test-Path -LiteralPath $private) -and (Test-Path -LiteralPath $perUser)) { $perUser } else { $private }
        $passFile = "$keystore.pass"
        if (-not (Test-Path -LiteralPath $keystore)) {
            $keytool = Find-Keytool
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $keystore) | Out-Null
            $storePass = -join ((1..32) | ForEach-Object { [char](Get-Random -InputObject ([int[]](48..57 + 65..90 + 97..122))) })
            [System.IO.File]::WriteAllText($passFile, $storePass, (New-Object System.Text.UTF8Encoding $false))
            Write-Host "No Android signing key found; making $keystore" -ForegroundColor Yellow
            & $keytool -genkeypair -keystore $keystore -alias $alias -keyalg RSA -keysize 2048 -validity 10000 `
                -storepass $storePass -keypass $storePass -dname "CN=KeyPlus, O=Berpiztu" | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "keytool failed with exit code $LASTEXITCODE" }
            Write-Host "Back up $keystore and its .pass file: every later APK has to be signed with this key to update the installed app." -ForegroundColor Yellow
        }
        if (-not $storePass) {
            if (-not (Test-Path -LiteralPath $passFile)) { throw "Keystore $keystore exists but $passFile is missing; set KEYPLUS_KEYSTORE_PASS." }
            $storePass = ([System.IO.File]::ReadAllText($passFile)).Trim()
        }
    } elseif (-not (Test-Path -LiteralPath $keystore)) {
        throw "KEYPLUS_KEYSTORE points to a missing file: $keystore"
    } elseif (-not $storePass) {
        throw "KEYPLUS_KEYSTORE is set; set KEYPLUS_KEYSTORE_PASS too."
    }
    if (-not $keyPass) { $keyPass = $storePass }
    return [pscustomobject]@{ Keystore = $keystore; StorePass = $storePass; Alias = $alias; KeyPass = $keyPass }
}

$project = Join-Path $PSScriptRoot "Berpiztu.KeyPlus.csproj"
$versionFile = Join-Path $PSScriptRoot "version.props"

$versionText = Get-Content $versionFile -Raw
$buildPattern = "(?<=<BuildNumber>)\d+(?=</BuildNumber>)"
$build = [int]([regex]::Match($versionText, $buildPattern).Value)
if (-not $NoBump) {
    $build++
    Set-Content $versionFile ([regex]::Replace($versionText, $buildPattern, "$build")) -NoNewline
}
$version = "1.0.0.$build"

$signing = Resolve-KeyPlusSigning
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$products = if ($Product -eq "Both") { @("Popup", "Keyboard") } else { @($Product) }
foreach ($keyPlusProduct in $products) {
    dotnet publish $project -c Release -nologo "-p:KeyPlusProduct=$keyPlusProduct" `
        "-p:AndroidKeyStore=true" "-p:AndroidSigningKeyStore=$($signing.Keystore)" "-p:AndroidSigningStorePass=$($signing.StorePass)" `
        "-p:AndroidSigningKeyAlias=$($signing.Alias)" "-p:AndroidSigningKeyPass=$($signing.KeyPass)"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($keyPlusProduct) failed with exit code $LASTEXITCODE" }

    $publish = Join-Path $PSScriptRoot "bin\$keyPlusProduct\Release\net10.0-android\publish"
    $apk = Get-ChildItem $publish -Filter "*-Signed.apk" -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $apk) { throw "The $keyPlusProduct build left no signed APK in $publish." }

    $copy = Join-Path $OutputDirectory "$(Get-ApkName $keyPlusProduct)-$version.apk"
    Copy-Item $apk.FullName $copy -Force
    Write-Host "APK ready: $copy (signed with $($signing.Keystore))"
}
