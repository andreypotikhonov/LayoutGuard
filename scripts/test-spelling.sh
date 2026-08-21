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
  Sources/LayoutGuard/Core/TypoCorrector.swift \
  Sources/LayoutGuard/Core/CorrectionEngine.swift \
  Tests/SpellingChecks.swift \
  -o .build/layoutguard-spelling-checks

.build/layoutguard-spelling-checks
