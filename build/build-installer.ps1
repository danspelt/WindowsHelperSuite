$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$srcPath = Join-Path $projectRoot "src"
$appProject = Join-Path $srcPath "WindowsHelperSuite.App\WindowsHelperSuite.App.csproj"
$publishPath = Join-Path $srcPath "WindowsHelperSuite.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
$installerScript = Join-Path $projectRoot "installer\WindowsHelperSuite.iss"
$artifactsDir = Join-Path $projectRoot "artifacts\installer"

Write-Host "=== Windows Helper Suite Installer Builder ===" -ForegroundColor Cyan
Write-Host ""

# Step 1: Clean
Write-Host "Step 1: Cleaning previous builds..." -ForegroundColor Yellow
dotnet clean $appProject -c Release -v quiet
if ($LASTEXITCODE -ne 0) { throw "Clean failed" }
Write-Host "Clean complete." -ForegroundColor Green
Write-Host ""

# Step 2: Publish
Write-Host "Step 2: Publishing self-contained application..." -ForegroundColor Yellow
dotnet publish $appProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=false `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:PublishReadyToRun=true `
    /p:PublishTrimmed=false `
    -v minimal

if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
Write-Host "Publish complete." -ForegroundColor Green
Write-Host ""

# Step 3: Verify publish output
if (-not (Test-Path $publishPath)) {
    throw "Publish directory not found: $publishPath"
}

$exePath = Join-Path $publishPath "WindowsHelperSuite.exe"
if (-not (Test-Path $exePath)) {
    throw "EXE not found at: $exePath"
}

$fileInfo = Get-Item $exePath
Write-Host "Published EXE: $exePath" -ForegroundColor Cyan
Write-Host "Size: $([math]::Round($fileInfo.Length / 1MB, 2)) MB" -ForegroundColor Cyan
Write-Host ""

# Step 4: Check for Inno Setup
Write-Host "Step 3: Building installer with Inno Setup..." -ForegroundColor Yellow

$isccPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 5\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 5\ISCC.exe"
)

$isccExe = $null
foreach ($path in $isccPaths) {
    if (Test-Path $path) {
        $isccExe = $path
        break
    }
}

if (-not $isccExe) {
    Write-Host ""
    Write-Host "WARNING: Inno Setup not found!" -ForegroundColor Yellow
    Write-Host "Please install Inno Setup from: https://jrsoftware.org/isinfo.php" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "After installing Inno Setup, run this script again or manually compile:" -ForegroundColor Yellow
    Write-Host "  $installerScript" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Application published successfully to:" -ForegroundColor Green
    Write-Host "  $publishPath" -ForegroundColor Cyan
    exit 0
}

Write-Host "Found Inno Setup: $isccExe" -ForegroundColor Cyan

# Step 5: Create artifacts directory
if (-not (Test-Path $artifactsDir)) {
    New-Item -ItemType Directory -Path $artifactsDir -Force | Out-Null
}

# Step 6: Build installer
Write-Host "Compiling installer script..." -ForegroundColor Yellow
& $isccExe $installerScript

if ($LASTEXITCODE -ne 0) { 
    throw "Installer compilation failed" 
}

Write-Host "Installer built successfully!" -ForegroundColor Green
Write-Host ""

# Step 7: Show results
$installerFiles = Get-ChildItem -Path $artifactsDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending
if ($installerFiles.Count -gt 0) {
    $latestInstaller = $installerFiles[0]
    Write-Host "=== SUCCESS ===" -ForegroundColor Green
    Write-Host ""
    Write-Host "Installer created:" -ForegroundColor Cyan
    Write-Host "  $($latestInstaller.FullName)" -ForegroundColor White
    Write-Host "  Size: $([math]::Round($latestInstaller.Length / 1MB, 2)) MB" -ForegroundColor White
    Write-Host ""
    Write-Host "You can now distribute this installer to users." -ForegroundColor Green
} else {
    Write-Host "WARNING: Installer file not found in artifacts directory" -ForegroundColor Yellow
}
