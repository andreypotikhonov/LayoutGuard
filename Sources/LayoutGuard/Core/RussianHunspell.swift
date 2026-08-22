import CHunspell
import CoreFoundation
import Foundation

final class RussianHunspell {
    static let shared = RussianHunspell()

    private let handle: OpaquePointer?
    private let dictionaryEncoding: String.Encoding

    private init() {
        let cfEncoding = CFStringConvertIANACharSetNameToEncoding("KOI8-R" as CFString)
        dictionaryEncoding = String.Encoding(
            rawValue: CFStringConvertEncodingToNSStringEncoding(cfEncoding)
        )

        let fileManager = FileManager.default
        let candidates = [
            Bundle.main.resourceURL?.appendingPathComponent("Dictionaries"),
            URL(fileURLWithPath: fileManager.currentDirectoryPath)
                .appendingPathComponent("Resources/Dictionaries")
        ].compactMap { $0 }

        guard let dictionaryDirectory = candidates.first(where: {
            fileManager.fileExists(atPath: $0.appendingPathComponent("ru_RU.aff").path) &&
            fileManager.fileExists(atPath: $0.appendingPathComponent("ru_RU.dic").path)
        }) else {
            handle = nil
            return
        }

        let affixPath = dictionaryDirectory.appendingPathComponent("ru_RU.aff").path
        let dictionaryPath = dictionaryDirectory.appendingPathComponent("ru_RU.dic").path
        handle = affixPath.withCString { affixCString in
            dictionaryPath.withCString { dictionaryCString in
                Hunspell_create(affixCString, dictionaryCString)
            }
        }
    }

    deinit {
        if let handle {
            Hunspell_destroy(handle)
        }
    }

    var isAvailable: Bool { handle != nil }

    func isCorrectlySpelled(_ word: String) -> Bool? {
        guard let handle, let encodedWord = encodedCString(for: word) else { return nil }
        return encodedWord.withUnsafeBufferPointer { buffer in
            Hunspell_spell(handle, buffer.baseAddress) != 0
        }
    }

    func suggestions(for word: String) -> [String]? {
        guard let handle, let encodedWord = encodedCString(for: word) else { return nil }

        var suggestionList: UnsafeMutablePointer<UnsafeMutablePointer<CChar>?>?
        let count = encodedWord.withUnsafeBufferPointer { buffer in
            Hunspell_suggest(handle, &suggestionList, buffer.baseAddress)
        }
        guard count > 0, suggestionList != nil else { return [] }
        defer { Hunspell_free_list(handle, &suggestionList, count) }

        return (0..<Int(count)).compactMap { index in
            guard let suggestion = suggestionList?[index] else { return nil }
            return String(cString: suggestion, encoding: dictionaryEncoding)
        }
    }

    private func encodedCString(for word: String) -> [CChar]? {
        guard let data = word.data(using: dictionaryEncoding) else { return nil }
        return data.map { CChar(bitPattern: $0) } + [0]
    }
}
