#Requires -Version 5.1
<#
.SYNOPSIS
  Publishes WindowsHelperSuite for Windows x64 (self-contained single-file).

  For version bump + staging under Releases\, use scripts\release.ps1.

.PARAMETER BuildInstaller
  If Inno Setup's ISCC.exe is on PATH, compiles installer\WindowsHelperSuite.iss after publish.

.EXAMPLE
  .\scripts\publish-release.ps1
  .\scripts\publish-release.ps1 -BuildInstaller
#>
[CmdletBinding()]
param(
    [switch] $BuildInstaller
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$appProj = Join-Path $repoRoot "src\WindowsHelperSuite.App\WindowsHelperSuite.App.csproj"

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

Push-Location $repoRoot
try {
    Write-Host "Publishing Release (profile Win64SelfContained)..." -ForegroundColor Cyan
    dotnet publish $appProj -c Release -p:PublishProfile=Win64SelfContained
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $publishDir = Join-Path $repoRoot "src\WindowsHelperSuite.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
    Get-ChildItem -Path $publishDir -Filter "*.pdb" -File -ErrorAction SilentlyContinue | Remove-Item -Force
    Write-Host "Output: $publishDir" -ForegroundColor Green

    if ($BuildInstaller) {
        $isccPath = Resolve-ISCCPath
        if (-not $isccPath) {
            Write-Warning "ISCC.exe not found on PATH. Install Inno Setup and add its folder to PATH, or compile installer\WindowsHelperSuite.iss manually."
            exit 0
        }
        $iss = Join-Path $repoRoot "installer\WindowsHelperSuite.iss"
        Write-Host "Compiling installer..." -ForegroundColor Cyan
        & $isccPath $iss
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        Write-Host "Installer output: $(Join-Path $repoRoot 'artifacts\installer')" -ForegroundColor Green
    }
}
finally {
    Pop-Location
}
