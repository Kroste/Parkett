#!/usr/bin/env bash
# Baut das AppImage aus einem fertigen linux-x64-Publish-Ordner (Kroste-Standard).
# Aufruf: packaging/linux/build-appimage.sh <version> <publish-dir>
# Hinweis: --appimage-extract-and-run ist nötig, weil im CI kein FUSE verfügbar ist.
set -euo pipefail

VERSION="$1"
PUBLISH_DIR="$2"
APPDIR="AppDir"

rm -rf "$APPDIR"
mkdir -p "$APPDIR/usr/bin"
cp -r "$PUBLISH_DIR"/* "$APPDIR/usr/bin/"
# Kleingeschrieben, weil die Dateien so heißen — auf Linux ist das nicht egal.
# Unter Windows lief dasselbe Skript jahrelang mit 'Parkett.desktop' durch.
cp packaging/linux/parkett.desktop "$APPDIR/"
cp Parkett/Assets/parkett.png "$APPDIR/"
cp packaging/linux/AppRun "$APPDIR/AppRun"
chmod +x "$APPDIR/AppRun" "$APPDIR/usr/bin/Parkett"

curl -sSL -o appimagetool \
  https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
chmod +x appimagetool
./appimagetool --appimage-extract-and-run "$APPDIR" "Parkett-${VERSION}-x86_64.AppImage"
echo "AppImage gebaut: Parkett-${VERSION}-x86_64.AppImage"
