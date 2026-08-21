import CoreGraphics
import Foundation

enum TextInjector {
    static let eventSignature: Int64 = 0x4C_47_55_41_52_44
    private static let backspaceKeyCode: CGKeyCode = 51

    static func replacePreviousText(utf16Length: Int, with replacement: String) {
        let source = CGEventSource(stateID: .combinedSessionState)

        for _ in 0..<utf16Length {
            postKey(source: source, keyCode: backspaceKeyCode, keyDown: true)
            postKey(source: source, keyCode: backspaceKeyCode, keyDown: false)
        }

        guard !replacement.isEmpty else { return }
        var utf16 = Array(replacement.utf16)

        let keyDown = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: true)
        keyDown?.setIntegerValueField(.eventSourceUserData, value: eventSignature)
        keyDown?.keyboardSetUnicodeString(stringLength: utf16.count, unicodeString: &utf16)
        keyDown?.post(tap: .cgSessionEventTap)

        let keyUp = CGEvent(keyboardEventSource: source, virtualKey: 0, keyDown: false)
        keyUp?.setIntegerValueField(.eventSourceUserData, value: eventSignature)
        keyUp?.post(tap: .cgSessionEventTap)
    }

    private static func postKey(source: CGEventSource?, keyCode: CGKeyCode, keyDown: Bool) {
        let event = CGEvent(keyboardEventSource: source, virtualKey: keyCode, keyDown: keyDown)
        event?.setIntegerValueField(.eventSourceUserData, value: eventSignature)
        event?.post(tap: .cgSessionEventTap)
    }
}
