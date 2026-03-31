# Release packaging (EXE + installer)

Repo-specific steps.

## Full release (version bump + publish + installer + `Releases\`)

From the repository root, with **Inno Setup** on `PATH`:

```powershell
.\scripts\release.ps1 -Version 1.0.1
```

This updates `WindowsHelperSuite.App.csproj` (`Version`, `AssemblyVersion`, `FileVersion`) and `installer/WindowsHelperSuite.iss` (`MyAppVersion`), publishes, compiles the installer, then creates:

```text
Releases\1.0.1\
  WindowsHelperSuiteSetup-1.0.1.exe
  publish\                 # full self-contained folder (same as bin\...\publish)
  release-notes.txt
```

Repackage **without** changing version files (uses current `<Version>` in the `.csproj`):

```powershell
.\scripts\release.ps1
```

Optional: `-Notes "..."` (multiline OK), `-SkipInstaller`, `-SkipStage` (build only, no `Releases\` copy).

The `Releases/` folder is gitignored (local bundles); keep changelog details in git via `docs/` or commit messages.

### Release checklist (manual)

- [ ] Decide version; run `release.ps1 -Version x.y.z -Notes "…"`.
- [ ] Run tests: `dotnet test`.
- [ ] Smoke-test `Releases\<version>\publish\WindowsHelperSuite.exe` (tray, overlay, hotkeys, settings).
- [ ] Run the staged installer on a clean VM or second PC if possible.
- [ ] Tag git: `git tag v1.0.1` (after commit of version bumps).
- [ ] Optional: code-sign installer and EXE before wide distribution.

## Prerequisites

- .NET 8 SDK
- Optional: [Inno Setup 6](https://jrsoftware.org/isinfo.php) (`ISCC.exe` on PATH for scripted builds)

## Publish (self-contained, single-file)

From the repository root:

```powershell
.\scripts\publish-release.ps1
```

Or:

```powershell
dotnet publish src/WindowsHelperSuite.App/WindowsHelperSuite.App.csproj -c Release -p:PublishProfile=Win64SelfContained
```

Output:

`src/WindowsHelperSuite.App/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/`

Main executable: **`WindowsHelperSuite.exe`** (`AssemblyName` in the app project).

The script removes stray `*.pdb` files from the publish folder so the Inno bundle stays lean. Plain `dotnet publish` may still leave PDBs next to the EXE; delete them before compiling the installer if you care about size.

## Installer (Inno Setup)

The `.iss` file uses **per-user** install (`PrivilegesRequired=lowest`): files go under `%LocalAppData%\Programs\Windows Helper Suite\`, no UAC prompt. For **Program Files**, edit `installer/WindowsHelperSuite.iss`: set `PrivilegesRequired=admin` and `DefaultDirName={autopf}\{#MyAppName}`.

1. Publish (above).
2. Open `installer/WindowsHelperSuite.iss` in Inno Setup and Build, **or** run:

   ```powershell
   .\scripts\publish-release.ps1 -BuildInstaller
   ```

3. Installer is written to `artifacts/installer/` (created by the script/compiler).

Use `.\scripts\release.ps1 -Version x.y.z` to keep `.csproj` and `.iss` in sync; or bump both by hand if you prefer.

## User data

Runtime data stays under `%AppData%\WindowsHelperSuite\` (settings, word bank, typing model, logs). The installer does not remove that folder on uninstall.

## Application icon

When you have `assets/icons/app.ico`, add to the app `.csproj`:

```xml
<ApplicationIcon>..\..\assets\icons\app.ico</ApplicationIcon>
```

(path relative to the project file)
