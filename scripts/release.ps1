#Requires -Version 5.1
<#
.SYNOPSIS
  Bump version (optional), publish, build installer, and copy artifacts to Releases\<version>\.

.PARAMETER Version
  Semantic version to write to the app .csproj and installer .iss (e.g. 1.0.1 or 1.0.1.0).
  If omitted, uses the version already in the .csproj and does not modify those files.

.PARAMETER Notes
  Extra text appended to Releases\<version>\release-notes.txt.

.PARAMETER SkipInstaller
  Publish only; do not run ISCC or copy an installer into Releases.

.PARAMETER SkipStage
  Build only; do not create Releases\<version>\ or copy files there.

.PARAMETER OpenInstaller
  After building the installer, open it (starts the Inno Setup wizard).

.PARAMETER OpenInstallerFolder
  After building the installer, open the output folder in File Explorer.

.EXAMPLE
  .\scripts\release.ps1 -Version 1.0.1
  .\scripts\release.ps1
  .\scripts\release.ps1 -Version 1.1.0 -Notes "- Fix tray focus`n- Update word bank defaults"
  .\scripts\release.ps1 -OpenInstaller
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string] $Version,

    [string] $Notes = "",

    [switch] $SkipInstaller,

    [switch] $SkipStage,

    [switch] $OpenInstaller,

    [switch] $OpenInstallerFolder
)

$ErrorActionPreference = "Stop"

function Resolve-ISCCPath {
    $cmd = Get-Command iscc -ErrorAction SilentlyContinue
    if ($cmd -and $cmd.Path -and (Test-Path -LiteralPath $cmd.Path)) {
        return $cmd.Path
    }

    $regCandidates = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1"
    )
    foreach ($key in $regCandidates) {
        try {
            $loc = (Get-ItemProperty -Path $key -ErrorAction Stop).InstallLocation
            if ($loc) {
                $p = Join-Path $loc "ISCC.exe"
                if (Test-Path -LiteralPath $p) { return $p }
            }
        } catch { }
    }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles} "Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 5\ISCC.exe"),
        (Join-Path ${env:ProgramFiles} "Inno Setup 5\ISCC.exe")
    ) | Where-Object { $_ -and $_.Trim() -ne "" } | Select-Object -Unique

    foreach ($p in $candidates) {
        if (Test-Path -LiteralPath $p) { return $p }
    }

    return $null
}

function Get-FourPartAssemblyVersion([string] $semVer) {
    $parts = $semVer.Split('.')
    while ($parts.Length -lt 4) { $parts += '0' }
    if ($parts.Length -gt 4) { $parts = $parts[0..3] }
    return ($parts -join '.')
}

function Get-VersionFromCsproj([string] $projPath) {
    $raw = Get-Content -LiteralPath $projPath -Raw
    if ($raw -notmatch '<Version>\s*([^<]+?)\s*</Version>') {
        throw "Could not find <Version> in $projPath"
    }
    return $Matches[1].Trim()
}

function Set-ProjectAndInstallerVersion {
    param(
        [string] $RepoRoot,
        [string] $SemVer
    )

    $four = Get-FourPartAssemblyVersion $SemVer
    $appProj = Join-Path $RepoRoot "src\WindowsHelperSuite.App\WindowsHelperSuite.App.csproj"
    $iss = Join-Path $RepoRoot "installer\WindowsHelperSuite.iss"

    $utf8 = New-Object System.Text.UTF8Encoding $false
    $projRaw = Get-Content -LiteralPath $appProj -Raw
    $projRaw = $projRaw -replace '(<Version>)[^<]*(</Version>)', "`$1$SemVer`$2"
    $projRaw = $projRaw -replace '(<AssemblyVersion>)[^<]*(</AssemblyVersion>)', "`$1$four`$2"
    $projRaw = $projRaw -replace '(<FileVersion>)[^<]*(</FileVersion>)', "`$1$four`$2"
    [System.IO.File]::WriteAllText($appProj, $projRaw, $utf8)

    $issRaw = Get-Content -LiteralPath $iss -Raw
    $issRaw = $issRaw -replace '(?m)^#define MyAppVersion "[^"]*"', "#define MyAppVersion `"$SemVer`""
    [System.IO.File]::WriteAllText($iss, $issRaw, $utf8)

    Write-Host "Set version to $SemVer (assembly/file $four) in .csproj and .iss" -ForegroundColor Green
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$appProj = Join-Path $repoRoot "src\WindowsHelperSuite.App\WindowsHelperSuite.App.csproj"
$publishDir = Join-Path $repoRoot "src\WindowsHelperSuite.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
$artifactsInstaller = Join-Path $repoRoot "artifacts\installer"

Push-Location $repoRoot
try {
    if ($Version) {
        Set-ProjectAndInstallerVersion -RepoRoot $repoRoot -SemVer $Version
    }

    $releaseVersion = if ($Version) { $Version } else { Get-VersionFromCsproj $appProj }
    Write-Host "Release version: $releaseVersion" -ForegroundColor Cyan

    Write-Host "Publishing Release (Win64SelfContained)..." -ForegroundColor Cyan
    dotnet publish $appProj -c Release -p:PublishProfile=Win64SelfContained
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    Get-ChildItem -Path $publishDir -Filter "*.pdb" -File -ErrorAction SilentlyContinue | Remove-Item -Force
    Write-Host "Publish output: $publishDir" -ForegroundColor Green

    $setupExe = Join-Path $artifactsInstaller "WindowsHelperSuiteSetup-$releaseVersion.exe"
    $setupExeStable = Join-Path $artifactsInstaller "WindowsHelperSuiteSetup.exe"

    if (-not $SkipInstaller) {
        $isccPath = Resolve-ISCCPath
        if (-not $isccPath) {
            throw "ISCC.exe not found. Install Inno Setup, add ISCC.exe to PATH, or use -SkipInstaller."
        }
        $iss = Join-Path $repoRoot "installer\WindowsHelperSuite.iss"
        Write-Host "Compiling installer..." -ForegroundColor Cyan
        & $isccPath $iss
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        if (-not (Test-Path -LiteralPath $setupExe)) {
            throw "Expected installer not found: $setupExe"
        }
        Write-Host "Installer: $setupExe" -ForegroundColor Green

        Copy-Item -LiteralPath $setupExe -Destination $setupExeStable -Force
        Write-Host "Installer (stable name): $setupExeStable" -ForegroundColor Green

        if ($OpenInstallerFolder) {
            Start-Process explorer.exe $artifactsInstaller | Out-Null
        }

        if ($OpenInstaller) {
            Start-Process -FilePath $setupExe | Out-Null
        }
    }

    if ($SkipStage) {
        return
    }

    $stageRoot = Join-Path $repoRoot "Releases\$releaseVersion"
    $stagePublish = Join-Path $stageRoot "publish"
    New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null
    if (Test-Path -LiteralPath $stagePublish) {
        Remove-Item -LiteralPath $stagePublish -Recurse -Force
    }

    Write-Host "Staging: $stageRoot" -ForegroundColor Cyan
    Copy-Item -Path $publishDir -Destination $stagePublish -Recurse -Force

    if (-not $SkipInstaller -and (Test-Path -LiteralPath $setupExe)) {
        Copy-Item -LiteralPath $setupExe -Destination (Join-Path $stageRoot (Split-Path $setupExe -Leaf)) -Force
    }

    $notesPath = Join-Path $stageRoot "release-notes.txt"
    $stamp = Get-Date -Format "yyyy-MM-dd HH:mm zzz"
    @(
        "Windows Helper Suite $releaseVersion"
        "Packaged: $stamp"
        ""
        "Contents:"
        "  publish/     Self-contained build (test WindowsHelperSuite.exe here)"
        if (-not $SkipInstaller) { "  WindowsHelperSuiteSetup-$releaseVersion.exe" } else { "  (installer skipped)" }
        ""
        if ($Notes) { $Notes.TrimEnd() } else { "(Add ship notes above this line for the next release.)" }
    ) | Set-Content -LiteralPath $notesPath -Encoding utf8

    Write-Host "Done. Staged under Releases\$releaseVersion\" -ForegroundColor Green
}
finally {
    Pop-Location
}
