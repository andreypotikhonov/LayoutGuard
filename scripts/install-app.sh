#!/bin/sh
set -eu

cd "$(dirname "$0")/.."

SOURCE_APP="$PWD/dist/LayoutGuard.app"
INSTALL_DIR="$HOME/Applications"
INSTALLED_APP="$INSTALL_DIR/LayoutGuard.app"

if [ ! -d "$SOURCE_APP" ]; then
    ./scripts/build-app.sh
fi

mkdir -p "$INSTALL_DIR"

if [ -e "$INSTALLED_APP" ]; then
    mv "$INSTALLED_APP" "$INSTALL_DIR/LayoutGuard.previous.app"
fi

ditto "$SOURCE_APP" "$INSTALLED_APP"
open "$INSTALLED_APP"

echo "$INSTALLED_APP"
