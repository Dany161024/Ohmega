#!/bin/bash
# build.sh — on a Mac: builds Ohmega.app (universal: Intel + Apple Silicon) and Ohmega-<version>.dmg.
#   macos/build.sh            build into macos/build/
#   macos/build.sh --install  …and put it into /Applications
# Needs the Command Line Tools (swiftc) and the FFmpeg binary in macos/vendor/ffmpeg
# (static build from evermeet.cx, see macos/README.md).
set -euo pipefail
cd "$(dirname "$0")"
VERSION=1.3.1
BUILD=build
APP="$BUILD/Ohmega.app"
rm -rf "$BUILD" && mkdir -p "$BUILD"

echo "== compiling (x86_64 + arm64)"
for arch in x86_64 arm64; do
    swiftc -O -swift-version 5 -parse-as-library -target "$arch-apple-macos13.0" \
        -o "$BUILD/Ohmega-$arch" Sources/*.swift
done
lipo -create -output "$BUILD/Ohmega" "$BUILD/Ohmega-x86_64" "$BUILD/Ohmega-arm64"

echo "== icon"
# AppIcon.png = ohmega.png on the macOS icon grid (make_icon.py), so it is as big as the other apps
ICONSET="$BUILD/Ohmega.iconset"
mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
    sips -z $size $size AppIcon.png --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
    sips -z $((size * 2)) $((size * 2)) AppIcon.png --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$BUILD/Ohmega.icns"

echo "== bundle"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BUILD/Ohmega" "$APP/Contents/MacOS/Ohmega"
cp "$BUILD/Ohmega.icns" "$APP/Contents/Resources/Ohmega.icns"
install -m 755 vendor/ffmpeg "$APP/Contents/Resources/ffmpeg"
cp ../LICENSE "$APP/Contents/Resources/LICENSE.txt"
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Ohmega</string>
  <key>CFBundleDisplayName</key><string>Ohmega</string>
  <key>CFBundleIdentifier</key><string>io.github.Daniel_v8.Ohmega</string>
  <key>CFBundleExecutable</key><string>Ohmega</string>
  <key>CFBundleIconFile</key><string>Ohmega</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VERSION</string>
  <key>CFBundleVersion</key><string>$VERSION</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>LSApplicationCategoryType</key><string>public.app-category.music</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>NSHumanReadableCopyright</key><string>GPL-3.0 · ohmega.is-my.app</string>
  <key>CFBundleDocumentTypes</key>
  <array>
    <dict>
      <key>CFBundleTypeName</key><string>Audio</string>
      <key>CFBundleTypeRole</key><string>Editor</string>
      <key>LSHandlerRank</key><string>Alternate</string>
      <key>LSItemContentTypes</key><array><string>public.audio</string></array>
    </dict>
    <dict>
      <key>CFBundleTypeName</key><string>Folder</string>
      <key>CFBundleTypeRole</key><string>Viewer</string>
      <key>LSHandlerRank</key><string>Alternate</string>
      <key>LSItemContentTypes</key><array><string>public.folder</string></array>
    </dict>
  </array>
</dict>
</plist>
PLIST

echo "== signing (ad-hoc: no Apple developer account)"
codesign --force -s - "$APP/Contents/Resources/ffmpeg"
codesign --force -s - "$APP"
codesign --verify --deep "$APP"
lipo -info "$APP/Contents/MacOS/Ohmega"

echo "== disk image"
STAGE="$BUILD/dmg"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
hdiutil create -quiet -volname "Ohmega $VERSION" -srcfolder "$STAGE" -ov -format UDZO "$BUILD/Ohmega-$VERSION.dmg"
ls -la "$BUILD/Ohmega-$VERSION.dmg"

if [ "${1:-}" = "--install" ]; then
    rm -rf /Applications/Ohmega.app
    cp -R "$APP" /Applications/
    echo "== installed to /Applications/Ohmega.app"
fi
