#!/bin/sh
set -eu

cd "$(dirname "$0")/.."

SOURCE_APP="$PWD/dist/LayoutGuard.app"
INSTALL_DIR="$HOME/Applications"
INSTALLED_APP="$INSTALL_DIR/LayoutGuard.app"
BACKUP_DIR="$INSTALL_DIR/LayoutGuard Backups"

if [ ! -d "$SOURCE_APP" ]; then
    ./scripts/build-app.sh
fi

mkdir -p "$INSTALL_DIR"

if [ -e "$INSTALLED_APP" ]; then
    pkill -x LayoutGuard 2>/dev/null || true
    mkdir -p "$BACKUP_DIR"
    BACKUP_APP="$BACKUP_DIR/LayoutGuard-$(date +%Y%m%d-%H%M%S).app"
    mv "$INSTALLED_APP" "$BACKUP_APP"
fi

ditto "$SOURCE_APP" "$INSTALLED_APP"
open "$INSTALLED_APP"

echo "$INSTALLED_APP"
