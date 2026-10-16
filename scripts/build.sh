#!/usr/bin/env bash
#
# Builds exam-unlock for Linux and Windows (Release, self-contained, so the
# target machine does not need the .NET runtime installed):
#
#   scripts/build.sh
#
# The output is placed in:
#   artifacts/exam-unlock-linux-x64/exam-unlock
#   artifacts/exam-unlock-win-x64/exam-unlock.exe
#
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLI="$ROOT/src/SafeExamBrowser.Linux.Cli"

echo "== building for linux-x64 (Release, self-contained) =="
dotnet publish "$CLI" -c Release -r linux-x64 --self-contained true -o "$ROOT/artifacts/exam-unlock-linux-x64" --nologo -v q

echo "== building for win-x64 (Release, self-contained) =="
dotnet publish "$CLI" -c Release -r win-x64 --self-contained true -o "$ROOT/artifacts/exam-unlock-win-x64" --nologo -v q

echo
echo "Done:"
echo "  $ROOT/artifacts/exam-unlock-linux-x64/exam-unlock"
echo "  $ROOT/artifacts/exam-unlock-win-x64/exam-unlock.exe"
