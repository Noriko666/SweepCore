$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = Join-Path $root "SweepCoreApp"
$outputDir = Join-Path $root "bin"
$assetsDir = Join-Path $root "Assets"
$iconFile = Join-Path $assetsDir "sweepcore.ico"
$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$references = @(
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\mscorlib.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\Microsoft.VisualBasic.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Core.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Drawing.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Xaml.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\WindowsBase.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationCore.dll",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\WPF\PresentationFramework.dll"
)

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "Compiler not found: $compiler"
}

if (-not (Test-Path -LiteralPath $iconFile)) {
    throw "Application icon not found: $iconFile"
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$sourceFiles = Get-ChildItem -LiteralPath $sourceDir -Filter *.cs | Select-Object -ExpandProperty FullName
if (-not $sourceFiles) {
    throw "No C# source files were found."
}

$args = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/optimize+",
    "/win32icon:$iconFile",
    "/out:$outputDir\\SweepCore.exe"
)

foreach ($reference in $references) {
    $args += "/reference:$reference"
}

$args += $sourceFiles

& $compiler $args

if ($LASTEXITCODE -ne 0) {
    throw "Build failed."
}

if (Test-Path -LiteralPath $assetsDir) {
    $outputAssetsDir = Join-Path $outputDir "Assets"
    New-Item -ItemType Directory -Force -Path $outputAssetsDir | Out-Null
    Get-ChildItem -LiteralPath $assetsDir -File | Copy-Item -Destination $outputAssetsDir -Force
}

Write-Host "Build successful: $outputDir\\SweepCore.exe"
