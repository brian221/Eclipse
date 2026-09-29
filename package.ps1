# Builds the Eclipse BigBox theme/plugin in Release and packages it into a
# distributable zip whose top-level folders (Plugins, StartupThemes, Themes)
# extract directly onto a LaunchBox installation directory.
#
# Usage:  powershell -ExecutionPolicy Bypass -File .\package.ps1

$ErrorActionPreference = 'Stop'

$root      = $PSScriptRoot
$project   = Join-Path $root 'Eclipse\Eclipse.csproj'
$stageRoot = Join-Path $root 'Eclipse\Eclipse\bin\Release\Eclipse\LaunchBox'
$distDir   = Join-Path $root 'dist'
$zipPath   = Join-Path $distDir 'Eclipse-Theme.zip'

# Locate the .NET SDK (dotnet) — it can build this legacy .NET Framework 4.8.1 WPF project.
$dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $dotnet) {
    $dotnet = 'C:\Program Files\dotnet\dotnet.exe'
}
if (-not (Test-Path $dotnet)) {
    throw "dotnet SDK not found. Install the .NET SDK (https://dotnet.microsoft.com/download)."
}

Write-Host "Building $project (Release)..." -ForegroundColor Cyan
& $dotnet build $project -c Release -v minimal
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path $stageRoot)) {
    throw "Staged theme folder not found at $stageRoot. Did the post-build step run?"
}

# Package the three install folders at the archive root so users can extract
# straight onto their LaunchBox folder (same layout as the original release).
New-Item -ItemType Directory -Force -Path $distDir | Out-Null
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

$folders = @('Plugins', 'StartupThemes', 'Themes') | ForEach-Object { Join-Path $stageRoot $_ }
Compress-Archive -Path $folders -DestinationPath $zipPath -CompressionLevel Optimal

$sizeMb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
Write-Host "Created $zipPath ($sizeMb MB)" -ForegroundColor Green
