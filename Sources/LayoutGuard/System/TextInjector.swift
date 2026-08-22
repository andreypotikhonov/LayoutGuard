import ApplicationServices
import CoreGraphics
import Foundation

enum TextInjector {
    static let eventSignature: Int64 = 0x4C_47_55_41_52_44
    private static let backspaceKeyCode: CGKeyCode = 51

    static func replacePreviousText(utf16Length: Int, with replacement: String) {
        if replaceUsingAccessibility(utf16Length: utf16Length, with: replacement) {
            return
        }

        let source = CGEventSource(stateID: .hidSystemState)

        for _ in 0..<utf16Length {
            postKey(source: source, keyCode: backspaceKeyCode, keyDown: true)
            postKey(source: source, keyCode: backspaceKeyCode, keyDown: false)
        }

        guard !replacement.isEmpty else { return }
        var utf16 = Array(replacement.utf16)

        let keyDown = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: true)
        keyDown?.setIntegerValueField(.eventSourceUserData, value: eventSignature)
        keyDown?.keyboardSetUnicodeString(stringLength: utf16.count, unicodeString: &utf16)
        keyDown?.post(tap: .cghidEventTap)

        let keyUp = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: false)
        keyUp?.setIntegerValueField(.eventSourceUserData, value: eventSignature)
        keyUp?.post(tap: .cghidEventTap)
    }

    private static func replaceUsingAccessibility(
        utf16Length: Int,
        with replacement: String
    ) -> Bool {
        let system = AXUIElementCreateSystemWide()
        var focusedValue: CFTypeRef?
        guard AXUIElementCopyAttributeValue(
            system,
            kAXFocusedUIElementAttribute as CFString,
            &focusedValue
        ) == .success,
        let focusedValue else {
            return false
        }

        let focused = unsafeBitCast(focusedValue, to: AXUIElement.self)
        var selectedRangeValue: CFTypeRef?
        guard AXUIElementCopyAttributeValue(
            focused,
            kAXSelectedTextRangeAttribute as CFString,
            &selectedRangeValue
        ) == .success,
        let selectedRangeValue,
        CFGetTypeID(selectedRangeValue) == AXValueGetTypeID() else {
            return false
        }

        let axRange = unsafeBitCast(selectedRangeValue, to: AXValue.self)
        var selectedRange = CFRange()
        guard AXValueGetValue(axRange, .cfRange, &selectedRange),
              selectedRange.location >= utf16Length else {
            return false
        }

        var replacementRange = CFRange(
            location: selectedRange.location - utf16Length,
            length: utf16Length + selectedRange.length
        )
        guard let replacementRangeValue = AXValueCreate(.cfRange, &replacementRange),
              AXUIElementSetAttributeValue(
                focused,
                kAXSelectedTextRangeAttribute as CFString,
                replacementRangeValue
              ) == .success else {
            return false
        }

        if AXUIElementSetAttributeValue(
            focused,
            kAXSelectedTextAttribute as CFString,
            replacement as CFString
        ) == .success {
            return true
        }

        var originalRange = selectedRange
        if let originalRangeValue = AXValueCreate(.cfRange, &originalRange) {
            _ = AXUIElementSetAttributeValue(
                focused,
                kAXSelectedTextRangeAttribute as CFString,
                originalRangeValue
            )
        }
        return false
    }

    private static func postKey(source: CGEventSource?, keyCode: CGKeyCode, keyDown: Bool) {
        let event = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: keyDown)
        event?.setIntegerValueField(.eventSourceUserData, value: eventSignature)
        event?.post(tap: .cghidEventTap)
    }
}
