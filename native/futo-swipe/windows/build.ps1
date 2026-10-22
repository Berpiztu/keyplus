<#
.SYNOPSIS
    Builds keyplus_swipe.dll, FUTO Swipe for Berpiztu AI Keyboard on Windows x64.
.DESCRIPTION
    Clones swipe-library (with its ExecuTorch submodule) into -Work the first
    time, makes a Python venv there with torchgen taken from torch's wheel
    (ExecuTorch's code generation, as FUTO's Makefile does), then configures
    and builds this folder's CMakeLists.txt with clang-cl and Ninja in Visual
    Studio's x64 environment, and copies the DLL to ..\win-x64\.
.EXAMPLE
    .\build.ps1 -Work C:\IA\Berpiztu\futo-swipe-build
#>
[CmdletBinding()]
param(
    # Where swipe-library, the venv and the build go; outside the repository,
    # and a short path: XNNPACK's object files outgrow Windows' 250 characters
    # under a long one.
    [Parameter(Mandatory)] [string]$Work
)

$ErrorActionPreference = "Stop"
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
$Library = Join-Path $Work "swipe-library"
$Venv = Join-Path $Work "venv"
$Build = Join-Path $Work "build-windows"

if (-not (Test-Path (Join-Path $Library "third_party\executorch\CMakeLists.txt"))) {
    git clone https://gitlab.futo.org/keyboard/swipe-library.git $Library
    git -C $Library -c core.longpaths=true submodule update --init --recursive --depth 1
}

$python = Join-Path $Venv "Scripts\python.exe"
if (-not (Test-Path $python)) {
    python -m venv $Venv
    & $python -m pip install --upgrade pip pyyaml typing_extensions
    & $python -m pip download --no-deps torch==2.10.0 --index-url https://download.pytorch.org/whl/cpu -d (Join-Path $Venv "tmp")
    $wheel = (Get-ChildItem (Join-Path $Venv "tmp\torch-*.whl"))[0].FullName
    & $python -c "import zipfile,sysconfig,sys; zf=zipfile.ZipFile(sys.argv[1]); sp=sysconfig.get_path('purelib'); [zf.extract(n,sp) for n in zf.namelist() if n.startswith('torchgen/')]" $wheel
    Remove-Item -Recurse -Force (Join-Path $Venv "tmp")
}

# Visual Studio's x64 environment: the linker, the Windows SDK and clang-cl.
$vs = & "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Llvm.Clang -property installationPath
& (Join-Path $vs "Common7\Tools\Launch-VsDevShell.ps1") -Arch amd64 -HostArch amd64 -SkipAutomaticLocation | Out-Null

cmake -G Ninja -S $Here -B $Build `
    -DCMAKE_BUILD_TYPE=Release `
    -DCMAKE_C_COMPILER=clang-cl -DCMAKE_CXX_COMPILER=clang-cl `
    -DPYTHON_EXECUTABLE="$python" `
    -DSWIPE_LIBRARY="$Library"
if ($LASTEXITCODE -ne 0) { throw "cmake configure failed" }
# flatc and flatcc first: ExecuTorch names their outputs without .exe, so
# Ninja does not know the step that makes them when another target asks.
cmake --build $Build --target flatbuffers_ep flatcc_ep
if ($LASTEXITCODE -ne 0) { throw "flatc build failed" }
cmake --build $Build --target keyplus_swipe
if ($LASTEXITCODE -ne 0) { throw "cmake build failed" }

$out = Join-Path $Here "..\win-x64"
New-Item -ItemType Directory -Force -Path $out | Out-Null
Copy-Item (Join-Path $Build "keyplus_swipe.dll") $out -Force
Get-Item (Join-Path $out "keyplus_swipe.dll")
