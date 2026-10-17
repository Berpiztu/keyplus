<#
.SYNOPSIS
    Build the KeyPlus APK, to copy to the phone and install there.
.DESCRIPTION
    Raises the build number in version.props by one (the version is
    1.0.0.<build>, rules.md), builds the Release APK (signed with the
    machine's Android debug key) and copies it to -OutputDirectory (dist by
    default) as KeyPlus-<version>.apk. Copy that file to the phone, open it to
    install it, then turn the service on in the phone's Settings: see
    README.md. Commit version.props with the build.
.EXAMPLE
    .\build-apk.ps1
.EXAMPLE
    .\build-apk.ps1 -OutputDirectory D:\Shared
#>
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "dist")
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "Berpiztu.KeyPlus.csproj"
$versionFile = Join-Path $PSScriptRoot "version.props"

$versionText = Get-Content $versionFile -Raw
$buildPattern = "(?<=<BuildNumber>)\d+(?=</BuildNumber>)"
$build = [int]([regex]::Match($versionText, $buildPattern).Value) + 1
Set-Content $versionFile ([regex]::Replace($versionText, $buildPattern, "$build")) -NoNewline
$version = "1.0.0.$build"

dotnet publish $project -c Release -nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$apk = Join-Path $PSScriptRoot "bin\Release\net10.0-android\publish\com.berpiztu.keyplus-Signed.apk"
if (-not (Test-Path $apk)) { throw "The build left no signed APK at $apk." }

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$target = Join-Path $OutputDirectory "KeyPlus-$version.apk"
Copy-Item $apk $target -Force
Write-Host "APK ready: $target"
