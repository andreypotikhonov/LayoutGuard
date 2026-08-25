import Foundation

@main
enum CoreChecks {
    static func main() {
        check(LayoutConverter.convert("ghbdtn", to: .russian) == "привет", "ghbdtn → привет")
        check(LayoutConverter.convert("руддщ", to: .english) == "hello", "руддщ → hello")
        check(LayoutConverter.convert("[jnz", to: .russian) == "хотя", "[jnz → хотя")
        check(LayoutConverter.language(of: "x") == .english, "recognize Latin x")
        check(LayoutConverter.language(of: "х") == .russian, "recognize Cyrillic х")
        check(LayoutConverter.language(of: "[") == nil, "do not treat [ as a Latin letter")
        check(LayoutConverter.language(of: "]") == nil, "do not treat ] as a Latin letter")
        check(LayoutConverter.language(of: "_") == nil, "do not treat _ as a Latin letter")
        check(!LayoutConverter.isLexicalWord("[l", language: .english), "reject bracket layout output")
        check(LayoutConverter.isLexicalWord("hello", language: .english), "accept an English word")
        check(
            LayoutConverter.convert("lf z levf", to: .russian) == "да я дума",
            "convert the whole sentence prefix"
        )
        check(LayoutDetector().correction(for: "ghbdtn")?.replacement == "привет", "detect Russian")
        check(LayoutDetector().correction(for: "руддщ")?.replacement == "hello", "detect English")
        check(LayoutDetector().correction(for: "rfr")?.replacement == "как", "detect short Russian word")
        check(LayoutDetector().correction(for: "ltkf")?.replacement == "дела", "detect second Russian word")
        check(LayoutDetector().correction(for: "hello") == nil, "keep valid English")
        check(LayoutDetector().correction(for: "привет") == nil, "keep valid Russian")
        check(LayoutDetector().correction(for: "today") == nil, "keep another valid English word")
        check(LayoutDetector().correction(for: "работа") == nil, "keep another valid Russian word")
        check(
            LayoutConverter.needsWordBoundary(between: "xcode", and: "х"),
            "separate Latin and Cyrillic words"
        )
        check(
            !LayoutConverter.needsWordBoundary(between: "xcode", and: "x"),
            "keep a Latin word together"
        )
        check(EditDistance.damerauLevenshtein("привет", "првиет") == 1, "transposition")
        print("Core checks passed")
    }

    private static func check(_ condition: @autoclosure () -> Bool, _ name: String) {
        guard condition() else {
            fatalError("Core check failed: \(name)")
        }
    }
}
