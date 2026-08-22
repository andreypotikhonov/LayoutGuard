import AppKit
import Foundation

final class TypoCorrector {
    private lazy var russianHunspell = RussianHunspell.shared
    private let languageScorer = LanguageScorer()

    private let acceptedWords: Set<String> = [
        "че", "чето", "че-то",
        "раскладка", "раскладки", "раскладке", "раскладку", "раскладкой"
    ]

    private let explicitCorrections: [String: String] = [
        "расскладка": "раскладка",
        "расскладки": "раскладки",
        "расскладке": "раскладке",
        "расскладку": "раскладку",
        "расскладкой": "раскладкой"
    ]

    func isCorrectlySpelled(_ word: String, language: SupportedLanguage) -> Bool {
        let normalized = word.lowercased()
        guard LayoutConverter.language(of: normalized) == language else { return false }
        if acceptedWords.contains(normalized) { return true }

        if language == .russian,
           let hunspellResult = russianHunspell.isCorrectlySpelled(normalized) {
            return hunspellResult
        }

        let misspelledRange = NSSpellChecker.shared.checkSpelling(
            of: normalized,
            startingAt: 0,
            language: language.spellCheckerCode,
            wrap: false,
            inSpellDocumentWithTag: 0,
            wordCount: nil
        )
        return misspelledRange.location == NSNotFound
    }

    func correction(for word: String, language: SupportedLanguage) -> String? {
        let normalized = word.lowercased()
        guard !acceptedWords.contains(normalized) else { return nil }

        if let explicit = explicitCorrections[normalized] {
            return matchingCase(of: word, replacement: explicit)
        }

        guard word.count >= 5,
              word.rangeOfCharacter(from: .letters.inverted) == nil else {
            return nil
        }

        let fullRange = NSRange(location: 0, length: (normalized as NSString).length)
        let guesses: [String]

        if language == .russian,
           let isCorrect = russianHunspell.isCorrectlySpelled(normalized),
           let hunspellGuesses = russianHunspell.suggestions(for: normalized) {
            guard !isCorrect else { return nil }
            guesses = hunspellGuesses + acceptedWords.sorted()
        } else {
            let misspelledRange = NSSpellChecker.shared.checkSpelling(
                of: normalized,
                startingAt: 0,
                language: language.spellCheckerCode,
                wrap: false,
                inSpellDocumentWithTag: 0,
                wordCount: nil
            )

            guard misspelledRange.location != NSNotFound,
                  misspelledRange == fullRange else {
                return nil
            }

            let systemGuesses = NSSpellChecker.shared.guesses(
                forWordRange: fullRange,
                in: normalized,
                language: language.spellCheckerCode,
                inSpellDocumentWithTag: 0
            ) ?? []
            guesses = systemGuesses + acceptedWords.sorted()
        }
        let maximumDistance = normalized.count >= 8 ? 2 : 1

        let ranked = guesses.enumerated().compactMap { index, candidate -> (String, Int, Double, Int)? in
            let normalizedCandidate = candidate.lowercased()
            guard normalizedCandidate != normalized,
                  LayoutConverter.language(of: normalizedCandidate) == language,
                  normalizedCandidate.rangeOfCharacter(from: .letters.inverted) == nil,
                  preservesWordEdges(
                    original: normalized,
                    candidate: normalizedCandidate
                  ) else {
                return nil
            }

            let distance = EditDistance.damerauLevenshtein(normalized, normalizedCandidate)
            guard distance <= maximumDistance else { return nil }
            return (
                candidate,
                distance,
                languageScorer.score(normalizedCandidate, as: language),
                index
            )
        }
        .sorted { lhs, rhs in
            if lhs.1 != rhs.1 { return lhs.1 < rhs.1 }
            if lhs.2 != rhs.2 { return lhs.2 > rhs.2 }
            return lhs.3 < rhs.3
        }

        guard let guess = ranked.first?.0 else { return nil }

        return matchingCase(of: word, replacement: guess)
    }

    private func preservesWordEdges(original: String, candidate: String) -> Bool {
        if candidate.first == original.first, candidate.last == original.last {
            return true
        }

        guard candidate.count == original.count + 1 else { return false }
        return String(candidate.dropFirst()) == original ||
            String(candidate.dropLast()) == original
    }

    private func matchingCase(of original: String, replacement: String) -> String {
        if original == original.uppercased() {
            return replacement.uppercased()
        }
        if let first = original.first, first.isUppercase {
            return replacement.prefix(1).uppercased() + replacement.dropFirst()
        }
        return replacement.lowercased()
    }
}
