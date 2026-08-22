#!/bin/sh
set -eu

cd "$(dirname "$0")/.."

APP_NAME="LayoutGuard"
APP_BUNDLE="$PWD/dist/$APP_NAME.app"
ZIP_PATH="$PWD/dist/$APP_NAME.zip"
NOTARY_PROFILE="${LAYOUTGUARD_NOTARY_PROFILE:-LayoutGuard-Notary}"
SIGNING_IDENTITY="${LAYOUTGUARD_SIGNING_IDENTITY:-}"

if [ -z "$SIGNING_IDENTITY" ]; then
    SIGNING_IDENTITIES=$(security find-identity -v -p codesigning | \
        sed -n 's/.*"\(Developer ID Application:[^"]*\)".*/\1/p')
    SIGNING_COUNT=$(printf '%s\n' "$SIGNING_IDENTITIES" | sed '/^$/d' | wc -l | tr -d ' ')
    if [ "$SIGNING_COUNT" -ne 1 ]; then
        echo "error: нужен ровно один сертификат Developer ID Application." >&2
        echo "Установите сертификат либо задайте LAYOUTGUARD_SIGNING_IDENTITY." >&2
        exit 1
    fi
    SIGNING_IDENTITY="$SIGNING_IDENTITIES"
fi

case "$SIGNING_IDENTITY" in
    "Developer ID Application:"*) ;;
    *)
        echo "error: для нотарификации требуется Developer ID Application." >&2
        exit 1
        ;;
esac

echo "Подпись: $SIGNING_IDENTITY"
LAYOUTGUARD_SIGNING_IDENTITY="$SIGNING_IDENTITY" ./scripts/build-app.sh

codesign --verify --deep --strict --verbose=2 "$APP_BUNDLE"
codesign -dvv "$APP_BUNDLE" 2>&1 | grep -E 'Authority=Developer ID Application|TeamIdentifier='

echo "Отправка в Apple Notary Service…"
xcrun notarytool submit "$ZIP_PATH" \
    --keychain-profile "$NOTARY_PROFILE" \
    --wait

echo "Прикрепление нотариального билета…"
xcrun stapler staple "$APP_BUNDLE"
xcrun stapler validate "$APP_BUNDLE"

rm -f "$ZIP_PATH"
ditto -c -k --sequesterRsrc --keepParent "$APP_BUNDLE" "$ZIP_PATH"

spctl --assess --type execute --verbose=4 "$APP_BUNDLE"
echo "$ZIP_PATH"
