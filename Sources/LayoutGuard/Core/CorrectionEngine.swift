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

    func decision(for word: String, correctTypos: Bool) -> CorrectionDecision? {
        if let layout = layoutDetector.correction(for: word) {
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
