import ApplicationServices
import Foundation

enum SecureFieldDetector {
    static func isSecureFieldFocused() -> Bool {
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
        var subroleValue: CFTypeRef?
        guard AXUIElementCopyAttributeValue(
            focused,
            kAXSubroleAttribute as CFString,
            &subroleValue
        ) == .success,
        let subrole = subroleValue as? String else {
            return false
        }

        return subrole == kAXSecureTextFieldSubrole as String
    }
}
