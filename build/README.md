# Build Scripts

This folder contains build and packaging scripts.

## Files

- `publish.ps1` - PowerShell script to publish the self-contained EXE
- `installer.iss` - Inno Setup script for creating the Windows installer

## Usage

### 1. Publish the app
```powershell
.\publish.ps1
```

### 2. Build the installer
1. Install Inno Setup from https://jrsoftware.org/isinfo.php
2. Open `installer.iss` in Inno Setup Compiler
3. Click Build → Compile
4. Output: `..\release\WindowsHelperSuite-Setup.exe`

## Release Process

1. Update version numbers in:
   - `installer.iss` (MyAppVersion)
   - `WindowsHelperSuite.App.csproj` (Version)

2. Run `publish.ps1`

3. Build installer with Inno Setup

4. Test on clean Windows VM

5. Tag release in git
