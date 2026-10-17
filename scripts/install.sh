#!/bin/sh
#
# Installs Safe Exam Browser for the current user (no root privileges required):
#
#   ./scripts/install.sh [install-prefix]
#
# The default prefix is ~/.local, so the binary ends up in ~/.local/bin and the
# desktop integration files in ~/.local/share. Pass e.g. /usr/local to install
# system-wide (requires write access to the prefix).

set -eu

PREFIX="${1:-$HOME/.local}"
ROOT="$(cd "$(dirname "$0")/.." && pwd)"

echo "Publishing Safe Exam Browser (Release)..."
dotnet publish "$ROOT/src/SafeExamBrowser.Linux.Cli" -c Release -r linux-x64 --self-contained true -o "$ROOT/artifacts/SafeExamBrowser" --nologo -v q

echo "Installing to $PREFIX..."
install -d "$PREFIX/bin" "$PREFIX/lib/SafeExamBrowser" "$PREFIX/share/applications" "$PREFIX/share/mime/packages"

cp -r "$ROOT/artifacts/SafeExamBrowser/." "$PREFIX/lib/SafeExamBrowser/"
ln -sf "$PREFIX/lib/SafeExamBrowser/SafeExamBrowser" "$PREFIX/bin/SafeExamBrowser"

# Remove files of a previous (renamed) installation, if any.
rm -rf "$PREFIX/lib/exam-unlock"
rm -f "$PREFIX/bin/exam-unlock" "$PREFIX/share/applications/exam-unlock.desktop" "$PREFIX/share/mime/packages/exam-unlock.xml"

# The Exec line is rewritten to the absolute path of the installed binary so that
# file managers find the client regardless of the desktop session's PATH.
sed "s|^Exec=.*|Exec=$PREFIX/bin/SafeExamBrowser run %u|" \
	"$ROOT/packaging/SafeExamBrowser.desktop" > "$PREFIX/share/applications/SafeExamBrowser.desktop"
chmod 644 "$PREFIX/share/applications/SafeExamBrowser.desktop"

install -m 644 "$ROOT/packaging/SafeExamBrowser.xml" "$PREFIX/share/mime/packages/SafeExamBrowser.xml"

if command -v update-desktop-database >/dev/null 2>&1; then
	update-desktop-database "$PREFIX/share/applications" 2>/dev/null || true
fi

if command -v update-mime-database >/dev/null 2>&1; then
	update-mime-database "$PREFIX/share/mime" 2>/dev/null || true
fi

echo
echo "Installed. Double-clicking a .seb configuration file (or choosing it as the"
echo "application to open with) starts the exam via the Safe Exam Browser. From the"
echo "command line (make sure '$PREFIX/bin' is in your PATH):"
echo
echo "  SafeExamBrowser check <configuration.seb>"
echo "  SafeExamBrowser keys  <configuration.seb>"
echo "  SafeExamBrowser run   <configuration.seb>"
