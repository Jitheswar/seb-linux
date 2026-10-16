#!/bin/sh
#
# Installs seb-linux for the current user (no root privileges required):
#
#   ./scripts/install.sh [install-prefix]
#
# The default prefix is ~/.local, so the binary ends up in ~/.local/bin and the
# desktop integration files in ~/.local/share. Pass e.g. /usr/local to install
# system-wide (requires write access to the prefix).

set -eu

PREFIX="${1:-$HOME/.local}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

echo "Publishing seb-linux (Release)..."
dotnet publish "$ROOT/src/SafeExamBrowser.Linux.Cli" -c Release -o "$ROOT/artifacts/seb-linux" --nologo -v q

echo "Installing to $PREFIX..."
install -d "$PREFIX/bin" "$PREFIX/lib/seb-linux" "$PREFIX/share/applications" "$PREFIX/share/mime/packages"

cp -r "$ROOT/artifacts/seb-linux/." "$PREFIX/lib/seb-linux/"
ln -sf "$PREFIX/lib/seb-linux/seb-linux" "$PREFIX/bin/seb-linux"

install -m 644 "$ROOT/packaging/seb-linux.desktop" "$PREFIX/share/applications/seb-linux.desktop"
install -m 644 "$ROOT/packaging/seb-linux-mime.xml" "$PREFIX/share/mime/packages/seb-linux.xml"

if command -v update-desktop-database >/dev/null 2>&1; then
	update-desktop-database "$PREFIX/share/applications" 2>/dev/null || true
fi

if command -v update-mime-database >/dev/null 2>&1; then
	update-mime-database "$PREFIX/share/mime" 2>/dev/null || true
fi

echo
echo "Installed. Make sure '$PREFIX/bin' is in your PATH, then run:"
echo
echo "  seb-linux check <configuration.seb>"
echo "  seb-linux keys  <configuration.seb>"
echo "  seb-linux run   <configuration.seb>"
