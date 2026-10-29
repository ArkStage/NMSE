#!/usr/bin/env bash
# ================================================================
# build-appimage.sh - Build an NMSE AppImage for Linux
#
# Creates a self-contained AppImage that bundles:
#   1. The NMSE Windows build (self-contained .NET, win-x64)
#   2. A minimal Wine installation for running the WinForms app
#
# Prerequisites (build host):
#   - Linux x86_64
#   - Wine 9.0+ installed (the script copies it into the AppImage)
#   - wget or curl
#   - NMSE published build (self-contained win-x64)
#
# Usage:
#   # First, publish NMSE on Windows (or cross-compile):
#   dotnet publish NMSE.csproj -c Release -r win-x64 --self-contained
#
#   # Then on Linux, build the AppImage:
#   ./build-appimage.sh /path/to/nmse-publish-output
#
# Output:
#   NMSE-x86_64.AppImage  (~300-500 MB)
#   CI renames this to NMSE-<version>-Release-x64.AppImage on release.
#
# Users just: chmod +x NMSE-x86_64.AppImage && ./NMSE-x86_64.AppImage
# ================================================================

set -euo pipefail

# Configuration
APPIMAGETOOL_URL="https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# Input validation
if [[ $# -lt 1 ]]; then
    echo "Usage: $0 <path-to-nmse-publish-output> [output-file]"
    echo ""
    echo "  <path-to-nmse-publish-output>  Directory containing NMSE.exe and Resources/"
    echo "                                  (output of 'dotnet publish -r win-x64 --self-contained')"
    echo "  [output-file]                  Optional output path (default: NMSE-x86_64.AppImage)"
    echo ""
    echo "Prerequisites:"
    echo "  - Linux x86_64 build host"
    echo "  - Wine 9.0+ installed system-wide (will be copied into AppImage)"
    echo "  - wget"
    exit 1
fi

NMSE_PUBLISH_DIR="$1"

if [[ ! -f "$NMSE_PUBLISH_DIR/NMSE.exe" ]]; then
    echo "ERROR: NMSE.exe not found in $NMSE_PUBLISH_DIR"
    echo "Run 'dotnet publish NMSE.csproj -c Release -r win-x64 --self-contained' first."
    exit 1
fi

ICON_SOURCE="$SCRIPT_DIR/nmse.png"
APPSTREAM_SOURCE="$SCRIPT_DIR/io.github.vectorcmdr.nmse.metainfo.xml"

if [[ ! -f "$ICON_SOURCE" ]]; then
    echo "ERROR: $ICON_SOURCE is missing."
    exit 1
fi

if ! file "$ICON_SOURCE" | grep -q "PNG image data, 256 x 256"; then
    echo "ERROR: $ICON_SOURCE is not a 256x256 PNG."
    file "$ICON_SOURCE" || true
    exit 1
fi

if [[ ! -f "$APPSTREAM_SOURCE" ]]; then
    echo "ERROR: $APPSTREAM_SOURCE is missing."
    exit 1
fi

read_version_field() {
    sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\([0-9][0-9]*\).*/\1/p" \
        "$REPO_ROOT/version.json" | head -n 1
}

NMSE_MAJOR="$(read_version_field major)"
NMSE_MINOR="$(read_version_field minor)"
NMSE_PATCH="$(read_version_field patch)"
NMSE_VERSION="${NMSE_MAJOR:-0}.${NMSE_MINOR:-0}.${NMSE_PATCH:-0}"
NMSE_DATE="$(date -u +%F)"

# Verify Wine is available
# On Ubuntu 24.04 the 'wine64' package installs to /usr/lib/wine/wine64
# (NOT /usr/bin/wine64).  The 'wine' meta-package provides /usr/bin/wine.
# We check PATH first, then the well-known lib path as a last resort.
WINE_CMD=""
if command -v wine >/dev/null 2>&1; then
    WINE_CMD="wine"
elif command -v wine64 >/dev/null 2>&1; then
    WINE_CMD="wine64"
elif [[ -x /usr/lib/wine/wine64 ]]; then
    WINE_CMD="/usr/lib/wine/wine64"
else
    echo "ERROR: Wine not found. Install Wine 9.0+ to build the AppImage."
    echo "  Ubuntu/Debian:  sudo apt install wine"
    echo "  Fedora:         sudo dnf install wine"
    echo "  Arch:           sudo pacman -S wine"
    exit 1
fi

WINE_VERSION="$("$WINE_CMD" --version 2>/dev/null | sed 's/wine-//')"
echo "[BUILD] Using Wine $WINE_VERSION (via $WINE_CMD)"

# Create AppDir structure
BUILD_DIR="$(mktemp -d)"
APPDIR="$BUILD_DIR/NMSE.AppDir"
echo "[BUILD] Working directory: $BUILD_DIR"

mkdir -p "$APPDIR"/{app,wine,usr/share/icons/hicolor/256x256/apps,usr/share/applications,usr/share/metainfo}

# Copy NMSE application
echo "[BUILD] Copying NMSE application ..."
cp -r "$NMSE_PUBLISH_DIR"/. "$APPDIR/app/"

# Copy Wine installation
echo "[BUILD] Copying Wine installation ..."

# On Ubuntu 24.04, Wine binaries and libraries are spread across:
#   /usr/bin/wine*                          (from 'wine' meta-package)
#   /usr/lib/wine/{wine64,wineserver64,...}  (from 'wine64' package)
#   /usr/lib/x86_64-linux-gnu/wine/         (64-bit PE DLLs)
#   /usr/lib/i386-linux-gnu/wine/           (32-bit PE DLLs)
#   /usr/share/wine/                        (shared data)
# On /opt installs (WineHQ PPA), everything is under one prefix.
#
# Strategy: try /opt prefixes first (self-contained), then fall back
# to assembling from the standard Debian/Ubuntu system paths.

WINE_PREFIX_DIR=""
for candidate in /opt/wine-stable /opt/wine; do
    if [[ -x "$candidate/bin/wine" ]] || [[ -x "$candidate/bin/wine64" ]]; then
        WINE_PREFIX_DIR="$candidate"
        break
    fi
done

if [[ -n "$WINE_PREFIX_DIR" ]]; then
    # Self-contained Wine installation (WineHQ PPA / opt layout)
    echo "[BUILD] Wine installation root: $WINE_PREFIX_DIR"
    if [[ -d "$WINE_PREFIX_DIR/bin" ]]; then
        cp -r "$WINE_PREFIX_DIR/bin" "$APPDIR/wine/"
    fi
    for libdir in lib lib64 lib/wine lib64/wine share/wine; do
        if [[ -d "$WINE_PREFIX_DIR/$libdir" ]]; then
            mkdir -p "$APPDIR/wine/$libdir"
            cp -r "$WINE_PREFIX_DIR/$libdir"/. "$APPDIR/wine/$libdir/" 2>/dev/null || true
        fi
    done
else
    # Debian/Ubuntu system layout: assemble Wine from standard paths
    echo "[BUILD] Wine installation root: /usr (system packages)"

    # Copy only Wine-related binaries from /usr/bin/
    # Use -L to dereference symlinks (e.g. /usr/bin/wine -> /etc/alternatives/wine)
    # so we get actual scripts/binaries rather than dangling symlinks in the AppDir.
    mkdir -p "$APPDIR/wine/bin"
    for bin in wine wine-stable wine64 wine64-preloader wine32 wine32-preloader \
               wineboot wineboot-stable winecfg winecfg-stable wineserver \
               wineserver-stable wineserver64; do
        src="/usr/bin/$bin"
        if [[ -e "$src" ]]; then
            cp -aL "$src" "$APPDIR/wine/bin/" 2>/dev/null || true
        fi
    done

    # Copy Wine core binaries from /usr/lib/wine/
    if [[ -d /usr/lib/wine ]]; then
        mkdir -p "$APPDIR/wine/lib/wine"
        cp -r /usr/lib/wine/. "$APPDIR/wine/lib/wine/" 2>/dev/null || true
    fi

    # Copy Wine PE DLLs and drivers from arch-specific directories
    for libdir in /usr/lib/x86_64-linux-gnu/wine /usr/lib/i386-linux-gnu/wine; do
        if [[ -d "$libdir" ]]; then
            dest="$APPDIR/wine${libdir#/usr}"
            mkdir -p "$dest"
            cp -r "$libdir"/. "$dest/" 2>/dev/null || true
        fi
    done

    # Copy shared Wine data
    if [[ -d /usr/share/wine ]]; then
        mkdir -p "$APPDIR/wine/share/wine"
        cp -r /usr/share/wine/. "$APPDIR/wine/share/wine/" 2>/dev/null || true
    fi
fi

# Ensure a 'wine' binary in the bundled bin/ works inside the AppDir.
# The system wine-stable script hardcodes /usr/lib/wine/ paths which
# won't exist inside the AppImage. Replace it with a wrapper that uses
# relative paths to find wine64 in the AppDir's lib/wine/.
if [[ -d "$APPDIR/wine/bin" ]] && [[ -x "$APPDIR/wine/lib/wine/wine64" ]]; then
    # Create a wrapper script that resolves wine64 relative to itself
    cat > "$APPDIR/wine/bin/wine" <<'WINEEOF'
#!/bin/sh -e
SELF_DIR="$(cd "$(dirname "$0")" && pwd)"
WINE64="$SELF_DIR/../lib/wine/wine64"
if [ -x "$WINE64" ]; then
    exec "$WINE64" "$@"
else
    echo "error: wine64 not found at $WINE64" >&2
    exit 1
fi
WINEEOF
    chmod +x "$APPDIR/wine/bin/wine"
    echo "[BUILD] Created wine wrapper in AppDir (delegates to lib/wine/wine64)"
elif [[ -d "$APPDIR/wine/bin" ]] && [[ ! -e "$APPDIR/wine/bin/wine" ]]; then
    if [[ -x "$APPDIR/wine/bin/wine64" ]]; then
        ln -s wine64 "$APPDIR/wine/bin/wine"
        echo "[BUILD] Created wine -> wine64 symlink in AppDir"
    fi
fi

# Desktop integration files
echo "[BUILD] Adding desktop integration files ..."

# Desktop entry
cp "$SCRIPT_DIR/nmse.desktop" "$APPDIR/nmse.desktop"
cp "$SCRIPT_DIR/nmse.desktop" "$APPDIR/usr/share/applications/nmse.desktop"

# AppRun entry point
cp "$SCRIPT_DIR/AppRun" "$APPDIR/AppRun"
chmod +x "$APPDIR/AppRun"

# The desktop file Exec points at nmse.sh; use the AppRun logic so the
# launcher also works when started from an installed desktop entry.
cp "$SCRIPT_DIR/AppRun" "$APPDIR/nmse.sh"
chmod +x "$APPDIR/nmse.sh"

# Icon - a committed, real 256x256 PNG (no ImageMagick conversion at build time)
cp "$ICON_SOURCE" "$APPDIR/nmse.png"
cp "$ICON_SOURCE" "$APPDIR/.DirIcon"
cp "$ICON_SOURCE" "$APPDIR/usr/share/icons/hicolor/256x256/apps/nmse.png"

# AppStream metadata for portal/software-centre integration and the AppImage catalog
sed -e "s/@VERSION@/${NMSE_VERSION}/g" \
    -e "s/@DATE@/${NMSE_DATE}/g" \
    "$APPSTREAM_SOURCE" > "$APPDIR/usr/share/metainfo/io.github.vectorcmdr.nmse.metainfo.xml"

# Download appimagetool
APPIMAGETOOL="$BUILD_DIR/appimagetool"
if [[ ! -x "$APPIMAGETOOL" ]]; then
    echo "[BUILD] Downloading appimagetool ..."
    wget -q -O "$APPIMAGETOOL" "$APPIMAGETOOL_URL"
    chmod +x "$APPIMAGETOOL"
fi

# Build AppImage
echo "[BUILD] Building AppImage ..."
OUTPUT_FILE="${2:-$SCRIPT_DIR/NMSE-x86_64.AppImage}"

# ARCH is required — appimagetool cannot guess it from the AppDir name.
export ARCH=x86_64

"$APPIMAGETOOL" "$APPDIR" "$OUTPUT_FILE"
echo "[BUILD] ✓ AppImage created: $OUTPUT_FILE"

echo "[BUILD] Size: $(du -sh "$OUTPUT_FILE" | cut -f1)"

# Cleanup
rm -rf "$BUILD_DIR"
echo "[BUILD] Done."
