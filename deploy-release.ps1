<#
.SYNOPSIS
    Publish a release: raise the build number, commit, tag and push it;
    GitHub Actions builds the two APKs and publishes the release
    (.github/workflows/release.yml).
.DESCRIPTION
    As wslc-ai-agent publishes its own:
      1. On main, up to date with GitHub, with nothing left uncommitted.
      2. Raises the build number in version.props by one (1.0.0.<build>).
      3. Commits version.props, tags v1.0.0.<build> and pushes both.
      4. GitHub Actions sees the tag, builds KeyPlus-Popup and
         Berpiztu-AI-Keyboard at that version, signed with the repository's
         key, and creates the GitHub release with the two APKs, to download
         and install on a phone or a headset. Nothing is uploaded from here;
         a failed build leaves the tag without a release: fix it and run the
         workflow again on the tag.
.EXAMPLE
    .\deploy-release.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

if ((git branch --show-current) -ne "main") { throw "A release is made from main." }
if (git status --porcelain --untracked-files=no) { throw "Commit or put aside the changes first: a release carries what main holds." }
git pull --rebase origin main
if ($LASTEXITCODE -ne 0) { throw "git pull failed with exit code $LASTEXITCODE" }

$versionFile = Join-Path $Root "version.props"
$versionText = Get-Content $versionFile -Raw
$buildPattern = "(?<=<BuildNumber>)\d+(?=</BuildNumber>)"
$build = [int]([regex]::Match($versionText, $buildPattern).Value) + 1
Set-Content $versionFile ([regex]::Replace($versionText, $buildPattern, "$build")) -NoNewline
$tag = "v1.0.0.$build"

git add version.props
git commit -m "Release $tag"
if ($LASTEXITCODE -ne 0) { throw "git commit failed with exit code $LASTEXITCODE" }
git tag $tag
git push origin main $tag
if ($LASTEXITCODE -ne 0) { throw "git push failed with exit code $LASTEXITCODE" }
Write-Host "Pushed ${tag}: GitHub Actions builds and publishes the release (gh run watch, or the Actions tab)." -ForegroundColor Green
