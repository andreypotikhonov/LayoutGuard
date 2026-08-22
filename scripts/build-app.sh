#!/bin/sh
set -eu

cd "$(dirname "$0")/.."

APP_NAME="LayoutGuard"
DIST_DIR="$PWD/dist"
APP_BUNDLE="$DIST_DIR/$APP_NAME.app"
CONTENTS="$APP_BUNDLE/Contents"
ICONSET="$DIST_DIR/AppIcon.iconset"
RELEASE_BINARY="$PWD/.build/release/$APP_NAME"

swift build -c release

rm -rf "$APP_BUNDLE" "$ICONSET"
mkdir -p "$CONTENTS/MacOS" "$CONTENTS/Resources" "$ICONSET"

cp "$RELEASE_BINARY" "$CONTENTS/MacOS/$APP_NAME"
cp App/Info.plist "$CONTENTS/Info.plist"
ditto Resources "$CONTENTS/Resources"
printf 'APPL????' > "$CONTENTS/PkgInfo"

swift scripts/generate-icon.swift "$DIST_DIR/AppIcon-1024.png"

for entry in "16 icon_16x16" "32 icon_16x16@2x" "32 icon_32x32" "64 icon_32x32@2x" "128 icon_128x128" "256 icon_128x128@2x" "256 icon_256x256" "512 icon_256x256@2x" "512 icon_512x512" "1024 icon_512x512@2x"; do
    set -- $entry
    sips -z "$1" "$1" "$DIST_DIR/AppIcon-1024.png" --out "$ICONSET/$2.png" >/dev/null
done

iconutil -c icns "$ICONSET" -o "$CONTENTS/Resources/AppIcon.icns"
rm -rf "$ICONSET"

LOCAL_SIGNING_IDENTITY="LayoutGuard Local Signing"
SIGNING_IDENTITY="${LAYOUTGUARD_SIGNING_IDENTITY:-$LOCAL_SIGNING_IDENTITY}"
if security find-identity -v -p codesigning | grep -Fq "\"$SIGNING_IDENTITY\""; then
    case "$SIGNING_IDENTITY" in
        "Developer ID Application:"*)
            codesign \
                --force \
                --options runtime \
                --timestamp \
                --sign "$SIGNING_IDENTITY" \
                "$APP_BUNDLE"
            ;;
        *)
            codesign --force --deep --sign "$SIGNING_IDENTITY" "$APP_BUNDLE"
            ;;
    esac
else
    if [ -n "${LAYOUTGUARD_SIGNING_IDENTITY:-}" ]; then
        echo "error: signing identity not found: $SIGNING_IDENTITY" >&2
        exit 1
    fi
    echo "warning: $LOCAL_SIGNING_IDENTITY not found; using an ad-hoc signature" >&2
    codesign --force --deep --sign - "$APP_BUNDLE"
fi
codesign --verify --deep --strict --verbose=2 "$APP_BUNDLE"

rm -f "$DIST_DIR/$APP_NAME.zip"
ditto -c -k --sequesterRsrc --keepParent "$APP_BUNDLE" "$DIST_DIR/$APP_NAME.zip"

echo "$APP_BUNDLE"
