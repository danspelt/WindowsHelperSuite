$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$srcPath = Join-Path $projectRoot "src"
$appProject = Join-Path $srcPath "WindowsHelperSuite.App\WindowsHelperSuite.App.csproj"
$publishPath = Join-Path $srcPath "WindowsHelperSuite.App\bin\Release\net8.0-windows\win-x64\publish"

Write-Host "=== Windows Helper Suite Publisher ===" -ForegroundColor Cyan
Write-Host ""

# Clean
Write-Host "Step 1: Cleaning..." -ForegroundColor Yellow
dotnet clean $appProject -c Release -v quiet
if ($LASTEXITCODE -ne 0) { throw "Clean failed" }
Write-Host "Clean complete." -ForegroundColor Green
Write-Host ""

# Publish
Write-Host "Step 2: Publishing self-contained EXE..." -ForegroundColor Yellow
dotnet publish $appProject `
    -c Release `
    -r win-x64 `
    --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:PublishReadyToRun=true `
    /p:PublishTrimmed=false `
    -v minimal

if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

Write-Host "Publish complete." -ForegroundColor Green
Write-Host ""

# Verify
if (Test-Path $publishPath) {
    $exePath = Join-Path $publishPath "WindowsHelperSuite.App.exe"
    if (Test-Path $exePath) {
        $fileInfo = Get-Item $exePath
        Write-Host "Output: $exePath" -ForegroundColor Cyan
        Write-Host "Size: $([math]::Round($fileInfo.Length / 1MB, 2)) MB" -ForegroundColor Cyan
        Write-Host ""
        Write-Host "=== SUCCESS ===" -ForegroundColor Green
        Write-Host "Next steps:"
        Write-Host "1. Open build\installer.iss in Inno Setup"
        Write-Host "2. Compile to create the installer"
    } else {
        Write-Host "WARNING: EXE not found at expected path" -ForegroundColor Yellow
    }
} else {
    Write-Host "ERROR: Publish directory not found" -ForegroundColor Red
}
