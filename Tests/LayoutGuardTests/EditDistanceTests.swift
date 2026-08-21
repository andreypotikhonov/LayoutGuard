import XCTest
@testable import LayoutGuard

final class EditDistanceTests: XCTestCase {
    func testEditOperations() {
        XCTAssertEqual(EditDistance.damerauLevenshtein("hello", "helo"), 1)
        XCTAssertEqual(EditDistance.damerauLevenshtein("привет", "првиет"), 1)
        XCTAssertEqual(EditDistance.damerauLevenshtein("word", "world"), 1)
        XCTAssertEqual(EditDistance.damerauLevenshtein("cat", "dog"), 3)
    }
}
