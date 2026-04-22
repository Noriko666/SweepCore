$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$distDir = Join-Path $root "dist"
$installerSource = Join-Path $root "Installer\SweepCoreSetupProgram.cs"
$appBuildScript = Join-Path $root "build.ps1"
$appBinary = Join-Path $root "bin\SweepCore.exe"
$logoFile = Join-Path $root "Assets\sweepcore-hero-logo.png"
$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Compiler not found: $compiler"
}

if (-not (Test-Path -LiteralPath $installerSource)) {
    throw "Installer source not found: $installerSource"
}

if (-not (Test-Path -LiteralPath $appBuildScript)) {
    throw "Application build script not found: $appBuildScript"
}

& powershell -ExecutionPolicy Bypass -File $appBuildScript
if ($LASTEXITCODE -ne 0) {
    throw "Application build failed."
}

if (-not (Test-Path -LiteralPath $appBinary)) {
    throw "Built application not found: $appBinary"
}

if (-not (Test-Path -LiteralPath $logoFile)) {
    throw "Logo asset not found: $logoFile"
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$outputFile = Join-Path $distDir "SweepCoreSetup.exe"
$references = @(
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Core.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Drawing.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Windows.Forms.dll"
)

$args = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/out:$outputFile",
    "/resource:$appBinary,SweepCore.Payload.SweepCore.exe",
    "/resource:$logoFile,SweepCore.Payload.sweepcore-hero-logo.png",
    $installerSource
)

foreach ($reference in $references) {
    $args += "/reference:$reference"
}

& $compiler $args

if ($LASTEXITCODE -ne 0) {
    throw "Installer build failed."
}

Write-Host "Installer build successful: $outputFile"
