import AppKit
import Foundation

final class TypoCorrector {
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
        let misspelledRange = NSSpellChecker.shared.checkSpelling(
            of: normalized,
            startingAt: 0,
            language: language.spellCheckerCode,
            wrap: false,
            inSpellDocumentWithTag: 0,
            wordCount: nil
        )

        guard misspelledRange.location != NSNotFound,
              misspelledRange == fullRange,
              let guesses = NSSpellChecker.shared.guesses(
                forWordRange: fullRange,
                in: normalized,
                language: language.spellCheckerCode,
                inSpellDocumentWithTag: 0
              ) else {
            return nil
        }

        guard let guess = guesses.first(where: {
            $0.lowercased() != normalized &&
            EditDistance.damerauLevenshtein(normalized, $0.lowercased()) == 1
        }) else { return nil }

        return matchingCase(of: word, replacement: guess)
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
