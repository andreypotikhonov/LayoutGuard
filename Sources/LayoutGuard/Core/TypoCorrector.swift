import AppKit
import Foundation

final class TypoCorrector {
    func correction(for word: String, language: SupportedLanguage) -> String? {
        guard word.count >= 4,
              word == word.lowercased(),
              word.rangeOfCharacter(from: .letters.inverted) == nil else {
            return nil
        }

        let fullRange = NSRange(location: 0, length: (word as NSString).length)
        let misspelledRange = NSSpellChecker.shared.checkSpelling(
            of: word,
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
                in: word,
                language: language.spellCheckerCode,
                inSpellDocumentWithTag: 0
              ) else {
            return nil
        }

        return guesses.first(where: {
            $0.lowercased() != word &&
            EditDistance.damerauLevenshtein(word, $0.lowercased()) == 1
        })
    }
}
