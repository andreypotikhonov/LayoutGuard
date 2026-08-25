import Foundation

struct LayoutCorrection: Equatable {
    let original: String
    let replacement: String
    let targetLanguage: SupportedLanguage
    let confidence: Double
}

struct LayoutDetector {
    private let scorer = LanguageScorer()
    private let minimumLength = 3
    private let minimumConfidence = 4.5

    func correction(for word: String) -> LayoutCorrection? {
        guard word.count >= minimumLength,
              let currentLanguage = LayoutConverter.language(of: word) else {
            return nil
        }

        let targetLanguage: SupportedLanguage = currentLanguage == .english ? .russian : .english
        guard let replacement = LayoutConverter.convert(word, to: targetLanguage),
              LayoutConverter.isLexicalWord(replacement, language: targetLanguage) else {
            return nil
        }

        let originalScore = scorer.score(word, as: currentLanguage)
        let replacementScore = scorer.score(replacement, as: targetLanguage)
        let confidence = replacementScore - originalScore

        guard confidence >= minimumConfidence else { return nil }
        return LayoutCorrection(
            original: word,
            replacement: replacement,
            targetLanguage: targetLanguage,
            confidence: confidence
        )
    }
}
