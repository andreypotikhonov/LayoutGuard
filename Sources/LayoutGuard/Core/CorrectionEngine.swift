import Foundation

struct CorrectionDecision: Equatable {
    enum Reason: Equatable {
        case wrongLayout
        case typo
    }

    let replacement: String
    let language: SupportedLanguage
    let reason: Reason
}

final class CorrectionEngine {
    private let layoutDetector = LayoutDetector()
    private let typoCorrector = TypoCorrector()

    func layoutDecision(for word: String) -> CorrectionDecision? {
        // This method runs while a key event is waiting to be delivered. Keep it
        // strictly in-memory: Hunspell and NSSpellChecker are reserved for the
        // full decision at a word boundary.
        guard word.count >= 5,
              let layout = layoutDetector.correction(for: word) else {
            return nil
        }
        return CorrectionDecision(
            replacement: layout.replacement,
            language: layout.targetLanguage,
            reason: .wrongLayout
        )
    }

    func isCorrectlySpelled(_ word: String, language: SupportedLanguage) -> Bool {
        typoCorrector.isCorrectlySpelled(word, language: language)
    }

    func decision(for word: String, correctTypos: Bool) -> CorrectionDecision? {
        var originalIsCorrectlySpelled = false
        if let currentLanguage = LayoutConverter.language(of: word) {
            let targetLanguage: SupportedLanguage = currentLanguage == .english ? .russian : .english
            originalIsCorrectlySpelled = typoCorrector.isCorrectlySpelled(
                word,
                language: currentLanguage
            )

            if !originalIsCorrectlySpelled,
               let converted = LayoutConverter.convert(word, to: targetLanguage),
               LayoutConverter.isLexicalWord(converted, language: targetLanguage) {
                if correctTypos,
                   word.count >= 7,
                   let correctedConverted = typoCorrector.correction(
                    for: converted,
                    language: targetLanguage
                   ) {
                    return CorrectionDecision(
                        replacement: correctedConverted,
                        language: targetLanguage,
                        reason: .wrongLayout
                    )
                }

                if typoCorrector.isCorrectlySpelled(converted, language: targetLanguage),
                   !originalIsCorrectlySpelled {
                    return CorrectionDecision(
                        replacement: converted,
                        language: targetLanguage,
                        reason: .wrongLayout
                    )
                }
            }
        }

        if !originalIsCorrectlySpelled,
           let layout = layoutDetector.correction(for: word) {
            return CorrectionDecision(
                replacement: layout.replacement,
                language: layout.targetLanguage,
                reason: .wrongLayout
            )
        }

        guard correctTypos, let language = LayoutConverter.language(of: word),
              let replacement = typoCorrector.correction(for: word, language: language) else {
            return nil
        }

        return CorrectionDecision(replacement: replacement, language: language, reason: .typo)
    }
}
