import Foundation

enum SupportedLanguage: String, CaseIterable, Codable {
    case english
    case russian

    var spellCheckerCode: String {
        switch self {
        case .english: return "en_US"
        case .russian: return "ru_RU"
        }
    }
}
