#Requires -Version 5.1
<#
.SYNOPSIS
    Build script for Live Captions Android APK
    
.DESCRIPTION
    This script sets up the Gradle wrapper (if needed) and builds the release APK.
    The resulting APK can be copied to your phone and installed.
    
.EXAMPLE
    .\BUILD-APK.ps1
#>

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Live Captions - Android Build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check for Java (required for Gradle)
$java = Get-Command java -ErrorAction SilentlyContinue
if (-not $java) {
    Write-Error "Java is not installed or not in PATH. Please install Java 17 or later."
    exit 1
}

# java -version outputs to stderr, so we need to capture it without error
$ErrorActionPreference = "Continue"
$javaVersionOutput = cmd /c "java -version 2>&1" | Select-String -Pattern '"(\d+)' | ForEach-Object { $_.Matches.Groups[1].Value } | Select-Object -First 1
$ErrorActionPreference = "Stop"
Write-Host "Java version: $javaVersionOutput" -ForegroundColor Green

# Download Gradle Wrapper if not present
$wrapperJar = Join-Path $projectRoot "gradle\wrapper\gradle-wrapper.jar"
$gradlewBat = Join-Path $projectRoot "gradlew.bat"

if (-not (Test-Path $wrapperJar) -or -not (Test-Path $gradlewBat)) {
    Write-Host "Setting up Gradle Wrapper..." -ForegroundColor Yellow
    
    # Create wrapper directory
    $wrapperDir = Join-Path $projectRoot "gradle\wrapper"
    if (-not (Test-Path $wrapperDir)) {
        New-Item -ItemType Directory -Path $wrapperDir -Force | Out-Null
    }
    
    # Download gradle wrapper files
    try {
        Invoke-WebRequest -Uri "https://services.gradle.org/distributions/gradle-8.9-bin.zip" -OutFile "$projectRoot\gradle-8.9-bin.zip" -UseBasicParsing
        
        # Extract and setup wrapper
        Expand-Archive -Path "$projectRoot\gradle-8.9-bin.zip" -DestinationPath "$projectRoot\gradle-temp" -Force
        $gradleHome = Join-Path $projectRoot "gradle-temp\gradle-8.9"
        $env:GRADLE_HOME = $gradleHome
        $env:PATH = "$gradleHome\bin;$env:PATH"
        
        # Generate wrapper using gradle
        Push-Location $projectRoot
        & "$gradleHome\bin\gradle.bat" wrapper --gradle-version 8.9
        Pop-Location
        
        # Cleanup
        Remove-Item "$projectRoot\gradle-8.9-bin.zip" -Force -ErrorAction SilentlyContinue
        Remove-Item "$projectRoot\gradle-temp" -Recurse -Force -ErrorAction SilentlyContinue
        
        Write-Host "Gradle Wrapper downloaded successfully!" -ForegroundColor Green
    }
    catch {
        Write-Error "Failed to download Gradle Wrapper. Error: $_"
        exit 1
    }
}

# Build the APK
Write-Host ""
Write-Host "Building Release APK..." -ForegroundColor Cyan
Write-Host "This may take a few minutes..." -ForegroundColor Yellow
Write-Host ""

Push-Location $projectRoot
try {
    & $gradlewBat assembleRelease --no-daemon --stacktrace
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed with exit code $LASTEXITCODE"
        exit 1
    }
    
    $apkPath = Join-Path $projectRoot "app\build\outputs\apk\release\app-release-unsigned.apk"
    
    if (Test-Path $apkPath) {
        Write-Host ""
        Write-Host "========================================" -ForegroundColor Green
        Write-Host "BUILD SUCCESSFUL!" -ForegroundColor Green
        Write-Host "========================================" -ForegroundColor Green
        Write-Host ""
        Write-Host "APK Location:" -ForegroundColor Cyan
        Write-Host $apkPath -ForegroundColor White
        Write-Host ""
        Write-Host "To install on your phone:" -ForegroundColor Yellow
        Write-Host "1. Copy the APK to your phone" -ForegroundColor White
        Write-Host "2. Enable 'Install from Unknown Sources' in Settings" -ForegroundColor White
        Write-Host "3. Tap the APK to install" -ForegroundColor White
        Write-Host ""
        Write-Host "Or use ADB:" -ForegroundColor Yellow
        Write-Host "adb install -r `"$apkPath`"" -ForegroundColor Gray
        Write-Host ""
    }
    else {
        Write-Error "APK not found at expected location: $apkPath"
        exit 1
    }
}
finally {
    Pop-Location
}
