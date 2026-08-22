import Foundation

enum LayoutConverter {
    private static let english = Array("qwertyuiop[]asdfghjkl;'zxcvbnm,.")
    private static let russian = Array("йцукенгшщзхъфывапролджэячсмитьбю")

    private static let englishToRussian = Dictionary(uniqueKeysWithValues: zip(english, russian))
    private static let russianToEnglish = Dictionary(uniqueKeysWithValues: zip(russian, english))

    static func convert(_ text: String, to language: SupportedLanguage) -> String? {
        let source = language == .russian ? englishToRussian : russianToEnglish
        var result = ""
        var changed = false

        for character in text {
            let lowercased = Character(String(character).lowercased())
            guard let mapped = source[lowercased] else {
                result.append(character)
                continue
            }

            changed = true
            let replacement = String(mapped)
            if String(character) == String(character).uppercased(),
               String(character) != String(character).lowercased() {
                result.append(contentsOf: replacement.uppercased())
            } else {
                result.append(contentsOf: replacement)
            }
        }

        return changed ? result : nil
    }

    static func language(of text: String) -> SupportedLanguage? {
        var latinCount = 0
        var cyrillicCount = 0

        for scalar in text.unicodeScalars {
            switch scalar.value {
            case 0x0041...0x007A: latinCount += 1
            case 0x0400...0x04FF: cyrillicCount += 1
            default: break
            }
        }

        guard max(latinCount, cyrillicCount) > 0 else { return nil }
        return latinCount >= cyrillicCount ? .english : .russian
    }

    static func needsWordBoundary(between left: String, and right: String) -> Bool {
        guard let leftLanguage = language(of: left),
              let rightLanguage = language(of: right) else {
            return false
        }
        return leftLanguage != rightLanguage
    }
}
