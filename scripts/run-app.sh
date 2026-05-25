#!/usr/bin/env bash
# Build and launch WindowsHelperSuite without blocking the shell (avoids dotnet run exit code 1).
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
PROJECT="$ROOT/src/WindowsHelperSuite.App/WindowsHelperSuite.App.csproj"
EXE="$ROOT/src/WindowsHelperSuite.App/bin/Debug/net8.0-windows10.0.19041.0/WindowsHelperSuite.exe"

taskkill.exe //F //IM WindowsHelperSuite.exe 2>/dev/null || true
dotnet build "$PROJECT" -v q
if [[ ! -f "$EXE" ]]; then
  echo "Missing $EXE" >&2
  exit 1
fi
start //B "" "$EXE"
echo "WindowsHelperSuite started — check the system tray."
