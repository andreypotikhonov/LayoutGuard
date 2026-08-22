#!/bin/sh
set -eu

PROFILE="${LAYOUTGUARD_NOTARY_PROFILE:-LayoutGuard-Notary}"

echo "Apple сохранит данные нотарификации в Связке ключей macOS."
echo "Понадобятся Apple ID, Team ID и app-specific password."
echo "Секретные значения не сохраняются в проекте или GitHub."
echo

xcrun notarytool store-credentials "$PROFILE"

echo
echo "Профиль $PROFILE сохранён. Теперь выполните ./scripts/notarize-app.sh"
