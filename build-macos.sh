#!/bin/bash
set -euo pipefail

root="$(cd "$(dirname "$0")" && pwd)"
output="$root/dist"
mkdir -p "$output"
staging="$(mktemp -d "$output/.macos-build.XXXXXX")"
trap 'rm -rf "$staging"' EXIT

app="$staging/ScreenSwitch.app"
contents="$app/Contents"
mkdir -p "$contents/MacOS" "$contents/Resources" "$staging/ScreenSwitch.iconset"

sdk="$(xcrun --sdk macosx --show-sdk-path)"
for arch in arm64 x86_64; do
    xcrun --sdk macosx swiftc -sdk "$sdk" -target "$arch-apple-macos13.0" \
        -swift-version 5 -warnings-as-errors -O -module-name ScreenSwitch \
        "$root"/macos/*.swift -o "$staging/ScreenSwitch-$arch"
done
xcrun lipo -create "$staging/ScreenSwitch-arm64" "$staging/ScreenSwitch-x86_64" \
    -output "$contents/MacOS/ScreenSwitch"

for size in 16 32 128; do
    sips -z "$size" "$size" "$root/logo.png" --out "$staging/ScreenSwitch.iconset/icon_${size}x${size}.png" >/dev/null
    sips -z "$((size * 2))" "$((size * 2))" "$root/logo.png" \
        --out "$staging/ScreenSwitch.iconset/icon_${size}x${size}@2x.png" >/dev/null
done
cp "$root/logo.png" "$staging/ScreenSwitch.iconset/icon_256x256.png"
iconutil -c icns "$staging/ScreenSwitch.iconset" -o "$contents/Resources/ScreenSwitch.icns"
cp "$root/logo.png" "$contents/Resources/ScreenSwitchLogo.png"
cp "$root/macos/Info.plist" "$contents/Info.plist"

codesign --force --sign "${SCREENSWITCH_SIGNING_IDENTITY:--}" "$app"
codesign --verify --strict "$app"
ditto -c -k --sequesterRsrc --keepParent "$app" "$staging/ScreenSwitch-macOS.zip"
rm -rf "$output/ScreenSwitch.app"
mv "$app" "$output/ScreenSwitch.app"
mv -f "$staging/ScreenSwitch-macOS.zip" "$output/ScreenSwitch-macOS.zip"
printf '%s\n' "$output/ScreenSwitch.app" "$output/ScreenSwitch-macOS.zip"
