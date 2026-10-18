<#
.SYNOPSIS
    Helpers shared by build-apk.ps1 and deploy-release.ps1. Dot-source it.
.DESCRIPTION
    As wslc-ai-agent's packaging\Packaging.ps1: the version (the release's in
    the csproj, a local build's in private\version.props) and the Android
    signing key's resolution.
#>

Set-StrictMode -Version Latest

function Get-KeyPlusRepoRoot {
    return (Split-Path -Parent $PSScriptRoot)
}

function Get-KeyPlusPrivateFolder {
    <# The checkout's private/ folder: the signing key and the local builds' versions. Git tracks nothing in it. #>
    return Join-Path (Get-KeyPlusRepoRoot) "private"
}

# ---------------------------------------------------------------- versions --

function Get-NextPatchVersion {
    param([Parameter(Mandatory = $true)][string]$Version)
    $parts = @()
    foreach ($piece in ($Version.Trim() -split '\.')) {
        if ($piece -ne "") { $parts += [int]$piece }
    }
    while ($parts.Count -lt 3) { $parts += 0 }
    $parts[2] = [int]$parts[2] + 1
    return ($parts[0..2] -join '.')
}

function Get-KeyPlusReleasePart {
    <# A version's first three parts, major.minor.patch: the release it is or counts from. #>
    param([Parameter(Mandatory = $true)][string]$Version)
    $parts = @()
    foreach ($piece in ($Version.Trim() -split '\.')) {
        if ($piece -ne "") { $parts += $piece }
    }
    while ($parts.Count -lt 3) { $parts += "0" }
    return ($parts[0..2] -join '.')
}

function Get-KeyPlusVersionCode {
    <#
    The Android versionCode of a release: its version as one number
    (1.0.40 is 100040000), leaving a local build counted from it the
    thousand that follow, so every later release is above all of them.
    #>
    param([Parameter(Mandatory = $true)][string]$Release)
    $parts = @((Get-KeyPlusReleasePart $Release) -split '\.' | ForEach-Object { [int]$_ })
    return $parts[0] * 100000000 + $parts[1] * 1000000 + $parts[2] * 1000
}

function Get-KeyPlusTrackedValue {
    <# The text of one element in a tracked project file: the release's version, which no build changes. #>
    param([string]$File, [string]$Element)
    $text = [System.IO.File]::ReadAllText($File)
    if ($text -notmatch "<$Element>([^<]+)</$Element>") {
        throw "No <$Element> in $File"
    }
    return $Matches[1].Trim()
}

function Set-KeyPlusTrackedValue {
    <# Writes one element of a tracked project file: only deploy-release.ps1 does, when a release is published. #>
    param([string]$File, [string]$Element, [string]$Value)
    $text = [System.IO.File]::ReadAllText($File)
    if ($text -notmatch "<$Element>([^<]+)</$Element>") {
        throw "No <$Element> in $File"
    }
    # The bare element only: the csproj's line that takes the local version
    # carries a Condition, so it is never this one.
    $text = ([regex]"<$Element>[^<]+</$Element>").Replace($text, "<$Element>$Value</$Element>", 1)
    [System.IO.File]::WriteAllText($File, $text, (New-Object System.Text.UTF8Encoding $false))
}

function Read-KeyPlusLocalVersions {
    <#
    The versions this checkout's builds have reached, from
    private\version.props: never tracked, so building APKs changes no
    tracked file, whoever builds. The csproj takes them when they are above
    the release's.
    #>
    $file = Join-Path (Get-KeyPlusPrivateFolder) "version.props"
    $values = @{}
    if (Test-Path -LiteralPath $file) {
        $text = [System.IO.File]::ReadAllText($file)
        foreach ($name in @("KeyPlusLocalVersion", "KeyPlusLocalBuild")) {
            if ($text -match "<$name>([^<]+)</$name>") { $values[$name] = $Matches[1].Trim() }
        }
    }
    return $values
}

function Write-KeyPlusLocalVersions {
    param([hashtable]$Values)
    $file = Join-Path (Get-KeyPlusPrivateFolder) "version.props"
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $file) | Out-Null
    $lines = @(
        "<Project>",
        "  <!-- The versions this checkout's builds have reached, written by the",
        "       packaging scripts; never tracked. Taken over the release's when above it. -->",
        "  <PropertyGroup>"
    )
    foreach ($name in @("KeyPlusLocalVersion", "KeyPlusLocalBuild")) {
        if ($Values.ContainsKey($name)) { $lines += "    <$name>$($Values[$name])</$name>" }
    }
    $lines += @("  </PropertyGroup>", "</Project>", "")
    [System.IO.File]::WriteAllText($file, ($lines -join "`r`n"), (New-Object System.Text.UTF8Encoding $false))
}

function Get-KeyPlusLocalBuildNumber {
    <#
    How many local builds this checkout has made of the release it is at: the
    fourth part of its local version when that counts from this release; 0
    when there is none, or it counts from an earlier release (a release
    starts the count again).
    #>
    param([string]$Release, [string]$Local)
    if (-not $Local) { return 0 }
    $parts = @($Local.Trim() -split '\.')
    if ($parts.Count -ne 4 -or (Get-KeyPlusReleasePart $Local) -ne (Get-KeyPlusReleasePart $Release)) { return 0 }
    return [int]$parts[3]
}

function Get-KeyPlusLocalVersion {
    <#
    A build's version: the release's, with the local build's number as a
    fourth part (1.0.40.3), so a build never takes a release's number and
    the next release, 1.0.41, is above every build of 1.0.40. -NoBump keeps
    the number of the last build; otherwise it is the next one.
    #>
    param([string]$Release, [string]$Local, [switch]$NoBump)
    $built = Get-KeyPlusLocalBuildNumber $Release $Local
    $number = if ($NoBump) { $built } else { $built + 1 }
    $base = Get-KeyPlusReleasePart $Release
    $version = if ($number -gt 0) { "$base.$number" } else { $base }
    return [pscustomobject]@{ Version = $version; Number = $number }
}

function Update-KeyPlusVersion {
    <#
    The APKs' version: the release's ApplicationDisplayVersion in the csproj
    with this checkout's build number as a fourth part, and the Android
    versionCode the release's code plus that number (1.0.40's third build is
    100040003), written to private\version.props only.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Csproj,
        [switch]$NoBump
    )
    $tracked = Get-KeyPlusTrackedValue $Csproj "ApplicationDisplayVersion"
    $local = Read-KeyPlusLocalVersions
    $build = Get-KeyPlusLocalVersion $tracked $local["KeyPlusLocalVersion"] -NoBump:$NoBump
    $code = if ($build.Number -gt 0) {
        (Get-KeyPlusVersionCode $tracked) + $build.Number
    } else {
        [int](Get-KeyPlusTrackedValue $Csproj "ApplicationVersion")
    }
    if (-not $NoBump) {
        $local["KeyPlusLocalVersion"] = $build.Version
        $local["KeyPlusLocalBuild"] = "$code"
        Write-KeyPlusLocalVersions $local
        Write-Host "Build $($build.Version) (versionCode $code): local build $($build.Number) of release $tracked (private\version.props)" -ForegroundColor Cyan
    }
    return [pscustomobject]@{ Display = $build.Version; Build = $code }
}

# ----------------------------------------------------------------- signing --

function Find-KeyPlusKeytool {
    $candidates = @()
    if ($env:JAVA_HOME) { $candidates += (Join-Path $env:JAVA_HOME "bin\keytool.exe") }
    $candidates += Get-ChildItem -Path "${env:ProgramFiles(x86)}\Android\openjdk", "$env:ProgramFiles\Microsoft\jdk-*", "$env:ProgramFiles\Android\jdk" -Filter keytool.exe -Recurse -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty FullName
    $cmd = Get-Command keytool -ErrorAction SilentlyContinue
    if ($cmd) { $candidates += $cmd.Source }
    foreach ($c in $candidates) {
        if ($c -and (Test-Path -LiteralPath $c)) { return $c }
    }
    throw "keytool.exe not found. Install a JDK (the Android workload's OpenJDK is enough) or set JAVA_HOME."
}

function Resolve-KeyPlusAndroidSigning {
    <#
    Android refuses to update an app whose new APK is signed with a different
    key, so every machine that builds one has to sign with the same key.
    Resolution order:
      1. KEYPLUS_KEYSTORE (+ KEYPLUS_KEYSTORE_PASS, KEYPLUS_KEY_ALIAS, KEYPLUS_KEY_PASS)
      2. private\android.keystore in this checkout, with its password in
         android.keystore.pass beside it
      3. %USERPROFILE%\.keyplus\android.keystore, the same pair of files,
         for a machine that keeps one key for several checkouts
    When none of them exists a new keystore is generated at (2). Back it up:
    an APK signed with any other key cannot update the installed app.
    #>
    $keystore = $env:KEYPLUS_KEYSTORE
    $storePass = $env:KEYPLUS_KEYSTORE_PASS
    $alias = if ($env:KEYPLUS_KEY_ALIAS) { $env:KEYPLUS_KEY_ALIAS } else { "keyplus" }
    $keyPass = $env:KEYPLUS_KEY_PASS

    if (-not $keystore) {
        $private = Join-Path (Get-KeyPlusPrivateFolder) "android.keystore"
        $perUser = Join-Path $env:USERPROFILE ".keyplus\android.keystore"
        $keystore = if (-not (Test-Path -LiteralPath $private) -and (Test-Path -LiteralPath $perUser)) { $perUser } else { $private }
        $passFile = "$keystore.pass"
        if (-not (Test-Path -LiteralPath $keystore)) {
            $keytool = Find-KeyPlusKeytool
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $keystore) | Out-Null
            $storePass = -join ((1..32) | ForEach-Object { [char](Get-Random -InputObject ([int[]](48..57 + 65..90 + 97..122))) })
            [System.IO.File]::WriteAllText($passFile, $storePass, (New-Object System.Text.UTF8Encoding $false))
            Write-Host "No Android signing key found; generating $keystore" -ForegroundColor Yellow
            & $keytool -genkeypair -v -keystore $keystore -alias $alias -keyalg RSA -keysize 2048 -validity 10000 `
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
