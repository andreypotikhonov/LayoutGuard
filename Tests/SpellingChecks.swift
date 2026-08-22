import Foundation

@main
enum SpellingChecks {
    static func main() {
        let engine = CorrectionEngine()

        check(engine.decision(for: "чето", correctTypos: true) == nil, "keep colloquial чето")
        check(
            engine.decision(for: "расскладку", correctTypos: true)?.replacement == "раскладку",
            "fix double с"
        )
        check(
            engine.decision(for: "првиет", correctTypos: true)?.replacement == "привет",
            "fix transposed letters"
        )
        check(
            engine.decision(for: "раскалдку", correctTypos: true)?.replacement == "раскладку",
            "fix a transposition in a longer word"
        )
        check(
            engine.decision(for: "раскалдкку", correctTypos: true)?.replacement == "раскладку",
            "fix two errors in a longer word"
        )
        check(
            engine.decision(for: "нпример", correctTypos: true)?.replacement == "например",
            "insert a missing letter instead of deleting the first letter"
        )
        check(
            engine.decision(for: "имер", correctTypos: true) == nil,
            "do not replace a short Russian typo with an unrelated word"
        )

        let russianLayout = engine.decision(for: "ghbdtn", correctTypos: true)
        check(russianLayout?.replacement == "привет", "detect Russian layout")
        check(russianLayout?.reason == .wrongLayout, "mark Russian layout switch")
        check(engine.layoutDecision(for: "ghbdt") == nil, "wait for a complete layout word")
        check(engine.layoutDecision(for: "ghbdtn")?.replacement == "привет", "live layout decision")
        check(engine.layoutDecision(for: "руддщ")?.replacement == "hello", "live reverse layout decision")
        check(engine.layoutDecision(for: "hel") == nil, "do not live-switch an English prefix")
        check(engine.layoutDecision(for: "hell") == nil, "keep a valid English prefix")
        check(engine.layoutDecision(for: "hello") == nil, "do not live-switch valid English")

        let combined = engine.decision(for: "hfccrkflre", correctTypos: true)
        check(combined?.replacement == "раскладку", "fix layout and typo together")
        check(combined?.reason == .wrongLayout, "mark combined layout switch")

        check(engine.decision(for: "hello", correctTypos: true) == nil, "keep valid English")
        check(engine.decision(for: "привет", correctTypos: true) == nil, "keep valid Russian")
        check(engine.decision(for: "пример", correctTypos: true) == nil, "keep another valid Russian word")
        check(engine.decision(for: "computer", correctTypos: true) == nil, "keep longer English word")
        check(engine.decision(for: "сегодня", correctTypos: true) == nil, "keep longer Russian word")
        print("Spelling checks passed")
    }

    private static func check(_ condition: @autoclosure () -> Bool, _ name: String) {
        guard condition() else { fatalError("Spelling check failed: \(name)") }
    }
}
