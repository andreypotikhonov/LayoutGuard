import XCTest
@testable import LayoutGuard

final class LayoutConverterTests: XCTestCase {
    func testEnglishKeysConvertToRussian() {
        XCTAssertEqual(LayoutConverter.convert("ghbdtn", to: .russian), "привет")
        XCTAssertEqual(LayoutConverter.convert("Rfr ltkf", to: .russian), "Как дела")
    }

    func testRussianKeysConvertToEnglish() {
        XCTAssertEqual(LayoutConverter.convert("руддщ", to: .english), "hello")
        XCTAssertEqual(LayoutConverter.convert("Цщкдв", to: .english), "World")
    }

    func testLanguageDetection() {
        XCTAssertEqual(LayoutConverter.language(of: "hello"), .english)
        XCTAssertEqual(LayoutConverter.language(of: "привет"), .russian)
    }
}
