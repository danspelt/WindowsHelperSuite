#Requires -Version 5.1
<#
.SYNOPSIS
    Sets up Android SDK for building the APK
#>

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "Setting up Android SDK..." -ForegroundColor Cyan

# Create android-sdk directory
$sdkRoot = Join-Path $projectRoot "android-sdk"
if (-not (Test-Path $sdkRoot)) {
    New-Item -ItemType Directory -Path $sdkRoot -Force | Out-Null
}

# Download command line tools
$cmdlineToolsUrl = "https://dl.google.com/android/repository/commandlinetools-win-11076708_latest.zip"
$cmdlineToolsZip = Join-Path $sdkRoot "cmdline-tools.zip"

Write-Host "Downloading Android command line tools..." -ForegroundColor Yellow
Invoke-WebRequest -Uri $cmdlineToolsUrl -OutFile $cmdlineToolsZip -UseBasicParsing

# Extract
Write-Host "Extracting..." -ForegroundColor Yellow
Expand-Archive -Path $cmdlineToolsZip -DestinationPath $sdkRoot -Force
Remove-Item $cmdlineToolsZip -Force

# Move to correct location
$cmdlineToolsDir = Join-Path $sdkRoot "cmdline-tools"
if (Test-Path (Join-Path $sdkRoot "cmdline-tools")) {
    $latestDir = Join-Path $cmdlineToolsDir "latest"
    if (-not (Test-Path $latestDir)) {
        New-Item -ItemType Directory -Path $latestDir -Force | Out-Null
    }
    # Move files from cmdline-tools to cmdline-tools/latest
    Get-ChildItem $cmdlineToolsDir -File | Move-Item -Destination $latestDir -Force
    Get-ChildItem $cmdlineToolsDir -Directory | Where-Object { $_.Name -ne "latest" } | Move-Item -Destination $latestDir -Force
}

# Set environment variables
$env:ANDROID_SDK_ROOT = $sdkRoot
$env:ANDROID_HOME = $sdkRoot
$env:PATH = "$sdkRoot\cmdline-tools\latest\bin;$env:PATH"

# Accept licenses
Write-Host "Accepting Android SDK licenses..." -ForegroundColor Yellow
"y" | & "$sdkRoot\cmdline-tools\latest\bin\sdkmanager.bat" --licenses 2>&1 | Out-Null

# Install required packages
Write-Host "Installing Android SDK components..." -ForegroundColor Yellow
& "$sdkRoot\cmdline-tools\latest\bin\sdkmanager.bat" "platforms;android-35" "build-tools;35.0.0" "platform-tools"

Write-Host "Android SDK setup complete!" -ForegroundColor Green
Write-Host "SDK Location: $sdkRoot" -ForegroundColor Cyan
