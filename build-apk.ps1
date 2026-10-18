<#
.SYNOPSIS
    Publish KeyPlus's two products to dist\KeyPlus-Popup.apk and
    dist\Berpiztu-AI-Keyboard.apk.
.DESCRIPTION
    As wslc-ai-agent's build-client-apk.ps1: bumps this checkout's build
    (a fourth part counted from the release's version in the csproj, and its
    versionCode, written to private\version.props only, so nothing tracked
    changes), publishes the signed Release APKs and copies them to dist\:
    KeyPlus-Popup.apk, the keys in a window above the keyboard in use, and
    Berpiztu-AI-Keyboard.apk, the keys as the keyboard, for any Android and
    for a headset (Galaxy XR). Each carries everything it runs, so it
    installs on its own: copy it to the device and open it there (README.md).
    -Product builds only one of them. -NoBump rebuilds the current version:
    the release's, on GitHub (.github/workflows/release.yml).

    The signing key comes from KEYPLUS_KEYSTORE, private\android.keystore
    or %USERPROFILE%\.keyplus\android.keystore, and is generated in
    private\ when missing (packaging\Packaging.ps1).
.EXAMPLE
    .\build-apk.ps1
.EXAMPLE
    .\build-apk.ps1 -Product Keyboard
#>
[CmdletBinding()]
param(
    # Popup, Keyboard, or both.
    [ValidateSet("Both", "Popup", "Keyboard")]
    [string]$Product = "Both",
    [switch]$NoBump
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $RepoRoot
. (Join-Path $RepoRoot "packaging\Packaging.ps1")

$Csproj = Join-Path $RepoRoot "Berpiztu.KeyPlus.csproj"
$Tfm = "net10.0-android"

# Each product's APK, as dist\ and the release name it.
function Get-ApkName {
    param([string]$Product)
    if ($Product -eq "Keyboard") { "Berpiztu-AI-Keyboard.apk" } else { "KeyPlus-Popup.apk" }
}

$signing = Resolve-KeyPlusAndroidSigning
$version = Update-KeyPlusVersion -Csproj $Csproj -NoBump:$NoBump
New-Item -ItemType Directory -Force -Path (Join-Path $RepoRoot "dist") | Out-Null

$products = if ($Product -eq "Both") { @("Popup", "Keyboard") } else { @($Product) }
foreach ($keyPlusProduct in $products) {
    Write-Host "Publishing KeyPlus $keyPlusProduct $($version.Display) (versionCode $($version.Build))..." -ForegroundColor Cyan
    dotnet publish $Csproj -c Release -f $Tfm -nologo "-p:KeyPlusProduct=$keyPlusProduct" `
        "-p:AndroidKeyStore=true" "-p:AndroidSigningKeyStore=$($signing.Keystore)" "-p:AndroidSigningStorePass=$($signing.StorePass)" `
        "-p:AndroidSigningKeyAlias=$($signing.Alias)" "-p:AndroidSigningKeyPass=$($signing.KeyPass)"
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish ($keyPlusProduct) failed with exit code $LASTEXITCODE" }

    $publish = Join-Path $RepoRoot "bin\$keyPlusProduct\Release\$Tfm\publish"
    $apk = Get-ChildItem -LiteralPath $publish -Filter "*-Signed.apk" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $apk) { throw "Publish succeeded but no signed APK was found under $publish" }

    $destination = Join-Path $RepoRoot "dist\$(Get-ApkName $keyPlusProduct)"
    Copy-Item -LiteralPath $apk.FullName -Destination $destination -Force
    Write-Host "$destination ready ($([math]::Round($apk.Length / 1MB, 1)) MB), versionName $($version.Display) / versionCode $($version.Build); signed with $($signing.Keystore)" -ForegroundColor Green
}
