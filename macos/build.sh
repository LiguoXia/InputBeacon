#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")"
VERSION="${VERSION:-1.1.0}"
if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "VERSION must be a numeric semantic version" >&2; exit 1
fi
export MACOSX_DEPLOYMENT_TARGET=13.0
OUTPUT="$PWD/dist"
APP="$OUTPUT/InputBeacon.app"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
for arch in arm64 x86_64; do
  swift build -c release --arch "$arch" --scratch-path ".build/$arch"
  bin_path="$(swift build -c release --arch "$arch" --scratch-path ".build/$arch" --show-bin-path)"
  cp "$bin_path/InputBeacon" "$OUTPUT/InputBeacon-$arch"
done
lipo -create "$OUTPUT/InputBeacon-arm64" "$OUTPUT/InputBeacon-x86_64" -output "$APP/Contents/MacOS/InputBeacon"
lipo "$APP/Contents/MacOS/InputBeacon" -verify_arch arm64 x86_64
cp Resources/Info.plist "$APP/Contents/Info.plist"
swift scripts/make-icon.swift "$OUTPUT/AppIcon.iconset"
iconutil -c icns "$OUTPUT/AppIcon.iconset" -o "$APP/Contents/Resources/AppIcon.icns"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$APP/Contents/Info.plist"
cp README.md "$APP/Contents/Resources/使用说明.md"
plutil -lint "$APP/Contents/Info.plist"
# Ad-hoc signing permits execution on Apple Silicon but is not Developer ID signing/notarization.
codesign --force --sign - "$APP"
codesign --verify --deep --strict --verbose=2 "$APP"
"$APP/Contents/MacOS/InputBeacon" --self-check
ditto -c -k --sequesterRsrc --keepParent "$APP" "$OUTPUT/InputBeacon-macOS-$VERSION-universal.zip"
STAGING="$(mktemp -d)"
trap 'rm -rf "$STAGING"' EXIT
ditto "$APP" "$STAGING/InputBeacon.app"
ln -s /Applications "$STAGING/Applications"
cp README.md "$STAGING/使用说明.md"
hdiutil create -volname InputBeacon -srcfolder "$STAGING" -ov -format UDZO "$OUTPUT/InputBeacon-macOS-$VERSION-universal.dmg"
(cd "$OUTPUT" && shasum -a 256 ./*.zip ./*.dmg > SHA256SUMS.txt)
