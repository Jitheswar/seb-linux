#!/usr/bin/env bash
#
# Builds Safe Exam Browser for Linux and Windows (Release, self-contained, so the
# target machine does not need the .NET runtime installed) and packages the
# release artifacts:
#
#   scripts/build.sh
#
# The self-contained publish output is placed in:
#   artifacts/SafeExamBrowser-linux-x64/SafeExamBrowser
#   artifacts/SafeExamBrowser-win-x64/SafeExamBrowser.exe
#
# The versioned release artifacts (in artifacts/release/) are:
#   SafeExamBrowser-<version>-linux-x64.tar.gz      generic Linux x64
#   SafeExamBrowser-<version>-arch-linux-x64.tar.gz Arch Linux
#   SafeExamBrowser-<version>-fedora-x64.tar.gz     Fedora
#   SafeExamBrowser-<version>-debian-x64.tar.gz     Debian
#   SafeExamBrowser-<version>-win-x64.zip           Windows x64
#
# The Linux payload is a single self-contained .NET 8 linux-x64 build, which
# runs on all supported glibc distros alike (the .NET 8 SDK does not support
# per-distro RIDs; glibc >= 2.23 is required, met by current Arch, Fedora,
# Debian and Ubuntu releases). The distro-specific archives carry the same
# payload, named for convenience.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CLI="$ROOT/src/SafeExamBrowser.Linux.Cli"
LINUX_OUT="$ROOT/artifacts/SafeExamBrowser-linux-x64"
WIN_OUT="$ROOT/artifacts/SafeExamBrowser-win-x64"
RELEASE_OUT="$ROOT/artifacts/release"
STAGE="$ROOT/artifacts/stage"

VERSION="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' "$ROOT/Directory.Build.props" | head -1)"

if [ -z "$VERSION" ]; then
	echo "FAIL: could not determine the version from Directory.Build.props" >&2
	exit 1
fi

echo "== building for linux-x64 (Release, self-contained) =="
dotnet publish "$CLI" -c Release -r linux-x64 --self-contained true -o "$LINUX_OUT" --nologo -v q

echo "== building for win-x64 (Release, self-contained) =="
dotnet publish "$CLI" -c Release -r win-x64 --self-contained true -o "$WIN_OUT" --nologo -v q

rm -rf "$RELEASE_OUT" "$STAGE"
mkdir -p "$RELEASE_OUT"

echo "== packaging release artifacts (version $VERSION) =="

package_linux() {
	local name="$1"

	rm -rf "$STAGE"
	cp -a "$LINUX_OUT" "$STAGE"
	tar -czf "$RELEASE_OUT/SafeExamBrowser-${VERSION}-${name}.tar.gz" \
		-C "$ROOT/artifacts" --transform "s|^stage$|SafeExamBrowser-${VERSION}-${name}|;s|^stage/|SafeExamBrowser-${VERSION}-${name}/|" stage
	rm -rf "$STAGE"
}

package_linux linux-x64
package_linux arch-linux-x64
package_linux fedora-x64
package_linux debian-x64

rm -rf "$STAGE"
cp -a "$WIN_OUT" "$ROOT/artifacts/SafeExamBrowser-${VERSION}-win-x64"
(
	cd "$ROOT/artifacts"
	zip -qr "$RELEASE_OUT/SafeExamBrowser-${VERSION}-win-x64.zip" "SafeExamBrowser-${VERSION}-win-x64"
)
rm -rf "$ROOT/artifacts/SafeExamBrowser-${VERSION}-win-x64"

echo
echo "Done:"
echo "  publish output:  $LINUX_OUT/SafeExamBrowser"
echo "                   $WIN_OUT/SafeExamBrowser.exe"
echo "  release archive:"
ls -1 "$RELEASE_OUT" | sed "s:^:    $RELEASE_OUT/:"
