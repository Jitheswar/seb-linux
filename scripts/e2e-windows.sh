#!/usr/bin/env bash
#
# End-to-end tests for the Windows build. The published win-x64 client runs
# under wine with Chrome for Testing (win64) as the browser and executes the
# same checks as scripts/e2e.sh.
#
#   scripts/e2e-windows.sh
#
# Wine 10 (the self-contained GE-Proton 10 build) is used instead of the system
# wine: the native wine 6.0.2 on RHEL 8 predates kernel32.GetProcessGroupAffinity,
# which the .NET 8 runtime calls during startup, so any .NET 8 application
# crashes under it. GE-Proton needs no installation (it is extracted into
# artifacts/win-e2e/ on first use, together with Chrome for Testing).
#
# The GE-Proton tree is split in a way that only works inside a Proton session:
# the child-process loader resolves the builtin DLLs from the prefix, so this
# script links them into the prefix's system32 directories (one-time).
#
# Requires a win-x64 publish output (scripts/build.sh, or a previous dotnet
# publish into artifacts/SafeExamBrowser-win-x64) and Xvfb.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
WIN_OUT="$ROOT/artifacts/SafeExamBrowser-win-x64"
E2E_WIN="$ROOT/artifacts/win-e2e"
PROTON_VERSION="${SEB_WIN_E2E_PROTON:-GE-Proton10-34}"
PROTON_DIR="$E2E_WIN/$PROTON_VERSION"
WINE64="$PROTON_DIR/files/bin/wine64"
CFT_DIR="$E2E_WIN/cft-win64"
CFT_ZIP="$E2E_WIN/chrome-win64.zip"
CFT_VERSION="${SEB_WIN_E2E_CFT:-121.0.6167.85}"
SCRATCH="$E2E_WIN/scratch"
WINEPREFIX="$SCRATCH/wine-prefix"
F_DIR="$E2E_WIN/fonts"

if [ ! -x "$WIN_OUT/SafeExamBrowser.exe" ]; then
	echo "FAIL: $WIN_OUT/SafeExamBrowser.exe not found — run scripts/build.sh first" >&2
	exit 1
fi

# Xvfb may only exist inside HPC tool paths; keep the first candidate whose
# shared-library dependencies are all resolvable.
XVFB="${SEB_WIN_E2E_XVFB:-}"
if [ -z "$XVFB" ]; then
	for candidate in $(find /home/apps /opt -maxdepth 8 -name Xvfb -type f 2>/dev/null); do
		if [ -z "$(ldd "$candidate" 2>&1 | grep 'not found')" ]; then
			XVFB="$candidate"
			break
		fi
	done
fi
[ -n "$XVFB" ] || { echo "FAIL: no working Xvfb found (override with SEB_WIN_E2E_XVFB)" >&2; exit 1; }

if [ ! -x "$WINE64" ]; then
	echo "downloading $PROTON_VERSION (self-contained wine, one-time)..."
	mkdir -p "$E2E_WIN"
	curl -sL -o "$E2E_WIN/$PROTON_VERSION.tar.gz" \
		"https://github.com/GloriousEggroll/proton-ge-custom/releases/download/$PROTON_VERSION/$PROTON_VERSION.tar.gz"
	tar -xzf "$E2E_WIN/$PROTON_VERSION.tar.gz" -C "$E2E_WIN"
fi
[ -x "$WINE64" ] || { echo "FAIL: $WINE64 is not executable" >&2; exit 1; }

if [ ! -x "$CFT_DIR/chrome-win64/chrome.exe" ]; then
	echo "preparing Chrome for Testing $CFT_VERSION (win64, one-time)..."
	mkdir -p "$CFT_DIR"
	if [ ! -f "$CFT_ZIP" ]; then
		curl -sL -o "$CFT_ZIP" \
			"https://storage.googleapis.com/chrome-for-testing-public/$CFT_VERSION/win64/chrome-win64.zip"
	fi
	unzip -q -o "$CFT_ZIP" -d "$CFT_DIR"
fi
# X bitmap fonts (one-time). Xvfb needs a resolvable "fixed" default font at
# startup; this machine only ships TrueType fonts, so take the PCF set from
# the RHEL 8 xorg-x11-fonts-misc RPM and add the canonical misc-fixed MLFB
# from X.Org.
if [ ! -f "$F_DIR/misc/fixf8" ]; then
	echo "preparing X bitmap fonts (one-time)..."
	rm -rf "$F_DIR" /tmp/seb-xfonts
	mkdir -p /tmp/seb-xfonts
	curl -sL -o /tmp/seb-xfonts/fonts.rpm \
		"https://vault.centos.org/centos/8/AppStream/x86_64/os/Packages/xorg-x11-fonts-misc-7.5-19.el8.noarch.rpm"
	rpm2cpio /tmp/seb-xfonts/fonts.rpm | cpio -idm -D /tmp/seb-xfonts 2>/dev/null
	mkdir -p "$F_DIR"
	cp -r /tmp/seb-xfonts/usr/share/X11/fonts/. "$F_DIR/"
	curl -sL -o "$F_DIR/misc/fixf8" \
		"https://gitlab.freedesktop.org/xorg/font/misc-fixed/-/raw/master/fixf8"
	for e in "$F_DIR"/encodings/*.enc.gz; do
		echo "../encodings/$(basename "$e")" >> "$F_DIR/misc/encodings.dir"
	done
	{ echo fixf8; ls "$F_DIR"/misc/*.pcf.gz | xargs -n1 basename; } > "$F_DIR/misc/INDEXES"
	mkfontdir "$F_DIR/misc"
fi
[ -f "$F_DIR/misc/fixf8" ] || { echo "FAIL: could not prepare X fonts" >&2; exit 1; }

mkdir -p "$SCRATCH"

# A private X display (try a few display numbers until one is free).
XVFB_PID=""
for DISPLAY_NUMBER in 99 98 97; do
	rm -f "/tmp/.X$DISPLAY_NUMBER-lock"
	"$XVFB" ":$DISPLAY_NUMBER" -screen 0 1280x800x24 -nolisten tcp -fp "$F_DIR/misc" &>/dev/null &
	XVFB_PID=$!
	sleep 1
	# A fatally failing Xvfb lingers as a zombie, so require the display
	# socket to actually exist.
	if [ -S "/tmp/.X11-unix/X$DISPLAY_NUMBER" ] && kill -0 "$XVFB_PID" 2>/dev/null; then
		export DISPLAY=":$DISPLAY_NUMBER"
		break
	fi
	kill "$XVFB_PID" 2>/dev/null || true
	XVFB_PID=""
done
[ -n "$XVFB_PID" ] || { echo "FAIL: could not start Xvfb" >&2; exit 1; }

cleanup() {
	[ -n "${XVFB_PID:-}" ] && kill "$XVFB_PID" 2>/dev/null || true
	# Make sure no wine/chrome process from this run survives.
	pkill -f "wine-server.*$WINEPREFIX" 2>/dev/null || true
	pkill -f "$CFT_DIR" 2>/dev/null || true
}
trap cleanup EXIT

export WINEPREFIX
export WINEDEBUG=-all

# The builtin DLLs must be resolvable from the prefix for the child-process
# loader (one-time setup).
link_builtins() {
	mkdir -p "$WINEPREFIX/drive_c/windows/system32" "$WINEPREFIX/drive_c/windows/syswow64"
	local f
	for f in "$PROTON_DIR/files/lib/wine/x86_64-windows"/*; do
		[ -e "$WINEPREFIX/drive_c/windows/system32/$(basename "$f")" ] || \
			ln -sf "$f" "$WINEPREFIX/drive_c/windows/system32/$(basename "$f")"
	done
	for f in "$PROTON_DIR/files/lib/wine/i386-windows"/*; do
		[ -e "$WINEPREFIX/drive_c/windows/syswow64/$(basename "$f")" ] || \
			ln -sf "$f" "$WINEPREFIX/drive_c/windows/syswow64/$(basename "$f")"
	done
}

if [ ! -d "$WINEPREFIX" ]; then
	echo "initializing the wine prefix (one-time)..."
	"$WINE64" wineboot -u
fi
link_builtins

# The harness drives the client like a binary; this wrapper runs the Windows
# executable through wine. DOTNET_SYSTEM_GLOBALIZATION_INVARIANT avoids a
# dependency on the host ICU (not installed on this machine).
EXE_WIN="Z:\\${WIN_OUT}\\SafeExamBrowser.exe"
cat > "$SCRATCH/cli-wrapper.sh" <<EOF
#!/bin/sh
export WINEPREFIX="$WINEPREFIX"
export WINEDEBUG=-all
export DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
unset LD_LIBRARY_PATH LD_PRELOAD
# Map the e2e harness's temporary HOME onto the emulated user profile so
# host-side checks (e.g. the downloaded file) see the same files.
case "\${HOME:-}" in
	/tmp/*|/var/tmp/*)
		WINE_USER_DIR="\$WINEPREFIX/drive_c/users/\$(id -un)"
		[ -e "\$WINE_USER_DIR" ] && rm -rf "\$WINE_USER_DIR"
		ln -sfn "\$HOME" "\$WINE_USER_DIR"
		;;
esac
exec "$WINE64" "$EXE_WIN" "\$@"
EOF
chmod +x "$SCRATCH/cli-wrapper.sh"

# The new-tab check reads the browser profile from the host filesystem; under
# wine the profile lives on the emulated drive, so it is disabled here (the
# same code path is covered by scripts/e2e.sh).
export SEB_E2E_NO_TAB_TEST=true
export SEB_E2E_CLI="$SCRATCH/cli-wrapper.sh"
# The harness unconditionally builds the Linux CLI; make the .NET SDK visible
# (it is a per-user install on this machine, not on the default PATH).
if [ -x "$HOME/.dotnet/dotnet" ]; then
	export PATH="$HOME/.dotnet:$PATH"
	export DOTNET_ROOT="$HOME/.dotnet"
fi
export SEB_CHROMIUM="Z:\\$CFT_DIR\\chrome-win64\\chrome.exe"
export SEB_HEADLESS=1

echo "running the Windows e2e scenarios..."
bash "$ROOT/scripts/e2e.sh"
