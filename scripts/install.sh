#!/bin/sh
#
# Installs exam-unlock for the current user (no root privileges required):
#
#   ./scripts/install.sh [install-prefix]
#
# The default prefix is ~/.local, so the binary ends up in ~/.local/bin and the
# desktop integration files in ~/.local/share. Pass e.g. /usr/local to install
# system-wide (requires write access to the prefix).

set -eu

PREFIX="${1:-$HOME/.local}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

echo "Publishing exam-unlock (Release)..."
dotnet publish "$ROOT/src/SafeExamBrowser.Linux.Cli" -c Release -o "$ROOT/artifacts/exam-unlock" --nologo -v q

echo "Installing to $PREFIX..."
install -d "$PREFIX/bin" "$PREFIX/lib/exam-unlock" "$PREFIX/share/applications" "$PREFIX/share/mime/packages"

cp -r "$ROOT/artifacts/exam-unlock/." "$PREFIX/lib/exam-unlock/"
ln -sf "$PREFIX/lib/exam-unlock/exam-unlock" "$PREFIX/bin/exam-unlock"

install -m 644 "$ROOT/packaging/exam-unlock.desktop" "$PREFIX/share/applications/exam-unlock.desktop"
install -m 644 "$ROOT/packaging/exam-unlock-mime.xml" "$PREFIX/share/mime/packages/exam-unlock.xml"

if command -v update-desktop-database >/dev/null 2>&1; then
	update-desktop-database "$PREFIX/share/applications" 2>/dev/null || true
fi

if command -v update-mime-database >/dev/null 2>&1; then
	update-mime-database "$PREFIX/share/mime" 2>/dev/null || true
fi

echo
echo "Installed. Make sure '$PREFIX/bin' is in your PATH, then run:"
echo
echo "  exam-unlock check <configuration.seb>"
echo "  exam-unlock keys  <configuration.seb>"
echo "  exam-unlock run   <configuration.seb>"
