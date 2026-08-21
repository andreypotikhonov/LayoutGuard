import Carbon.HIToolbox
import Foundation

enum InputSourceController {
    static func select(_ language: SupportedLanguage) -> Bool {
        if currentLanguage() == language { return true }

        let filter = [
            kTISPropertyInputSourceCategory as String: kTISCategoryKeyboardInputSource!,
            kTISPropertyInputSourceIsSelectCapable as String: true
        ] as CFDictionary

        guard let unmanaged = TISCreateInputSourceList(filter, false) else { return false }
        let sources = unmanaged.takeRetainedValue() as NSArray

        for case let source as TISInputSource in sources {
            let identifier = stringProperty(kTISPropertyInputSourceID, source: source) ?? ""
            let name = stringProperty(kTISPropertyLocalizedName, source: source) ?? ""
            let haystack = "\(identifier) \(name)".lowercased()

            let matches: Bool
            switch language {
            case .english:
                matches = identifier.lowercased().contains("keylayout.us") ||
                    identifier.lowercased().contains("keylayout.abc") ||
                    name.lowercased() == "abc" ||
                    name.lowercased() == "u.s."
            case .russian:
                matches = identifier.lowercased().contains("keylayout.russian") ||
                    haystack.contains("russian") ||
                    name.lowercased().contains("рус")
            }

            if matches {
                guard TISSelectInputSource(source) == noErr else { return false }
                return currentLanguage() == language
            }
        }

        return false
    }

    static func currentLanguage() -> SupportedLanguage? {
        guard let source = TISCopyCurrentKeyboardInputSource()?.takeRetainedValue(),
              let identifier = stringProperty(kTISPropertyInputSourceID, source: source)?.lowercased() else {
            return nil
        }

        if identifier.contains("keylayout.russian") { return .russian }
        if identifier.contains("keylayout.abc") || identifier.contains("keylayout.us") { return .english }
        return nil
    }

    private static func stringProperty(_ key: CFString, source: TISInputSource) -> String? {
        guard let pointer = TISGetInputSourceProperty(source, key) else { return nil }
        return Unmanaged<CFString>.fromOpaque(pointer).takeUnretainedValue() as String
    }
}
