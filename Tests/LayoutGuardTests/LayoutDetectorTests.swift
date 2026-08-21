import XCTest
@testable import LayoutGuard

final class LayoutDetectorTests: XCTestCase {
    private let detector = LayoutDetector()

    func testDetectsWrongEnglishLayoutForRussianWord() {
        let result = detector.correction(for: "ghbdtn")
        XCTAssertEqual(result?.replacement, "привет")
        XCTAssertEqual(result?.targetLanguage, .russian)
    }

    func testDetectsWrongRussianLayoutForEnglishWord() {
        let result = detector.correction(for: "руддщ")
        XCTAssertEqual(result?.replacement, "hello")
        XCTAssertEqual(result?.targetLanguage, .english)
    }

    func testDoesNotChangeValidWords() {
        XCTAssertNil(detector.correction(for: "hello"))
        XCTAssertNil(detector.correction(for: "привет"))
    }
}
