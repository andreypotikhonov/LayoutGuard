#!/bin/sh
set -eu

cd "$(dirname "$0")/.."
mkdir -p .build

swiftc \
  Sources/LayoutGuard/Core/SupportedLanguage.swift \
  Sources/LayoutGuard/Core/LayoutConverter.swift \
  Sources/LayoutGuard/Core/LanguageScorer.swift \
  Sources/LayoutGuard/Core/LayoutDetector.swift \
  Sources/LayoutGuard/Core/EditDistance.swift \
  Tests/CoreChecks.swift \
  -o .build/layoutguard-core-checks

.build/layoutguard-core-checks
