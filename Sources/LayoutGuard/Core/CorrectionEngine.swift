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
        guard let decision = decision(for: word, correctTypos: true),
              decision.reason == .wrongLayout else {
            return nil
        }
        return decision
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
               let converted = LayoutConverter.convert(word, to: targetLanguage) {
                if correctTypos,
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
