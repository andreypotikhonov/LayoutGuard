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

    static func isBrowserAddressFieldFocused(applicationIdentifier: String?) -> Bool {
        guard isBrowser(applicationIdentifier) else { return false }

        let system = AXUIElementCreateSystemWide()
        var focusedValue: CFTypeRef?
        guard AXUIElementCopyAttributeValue(
            system,
            kAXFocusedUIElementAttribute as CFString,
            &focusedValue
        ) == .success,
        let focusedValue,
        CFGetTypeID(focusedValue) == AXUIElementGetTypeID() else {
            return false
        }

        let focused = unsafeBitCast(focusedValue, to: AXUIElement.self)
        let role = stringAttribute(kAXRoleAttribute as CFString, of: focused)
        guard role == kAXTextFieldRole as String || role == kAXComboBoxRole as String else {
            return false
        }

        let identifyingText = [
            stringAttribute(kAXDescriptionAttribute as CFString, of: focused),
            stringAttribute(kAXTitleAttribute as CFString, of: focused),
            stringAttribute(kAXHelpAttribute as CFString, of: focused),
            stringAttribute("AXIdentifier" as CFString, of: focused)
        ]
        .compactMap { $0 }
        .joined(separator: " ")
        .lowercased()

        let addressMarkers = ["address", "location", "url", "omnibox"]
        if addressMarkers.contains(where: identifyingText.contains) {
            return true
        }

        // Safari and Chromium browsers expose their omnibox inside a toolbar,
        // even when the field itself has no useful description.
        var ancestor = focused
        for _ in 0..<6 {
            var parentValue: CFTypeRef?
            guard AXUIElementCopyAttributeValue(
                ancestor,
                kAXParentAttribute as CFString,
                &parentValue
            ) == .success,
            let parentValue,
            CFGetTypeID(parentValue) == AXUIElementGetTypeID() else {
                break
            }
            let parent = unsafeBitCast(parentValue, to: AXUIElement.self)
            if stringAttribute(kAXRoleAttribute as CFString, of: parent) == kAXToolbarRole as String {
                return true
            }
            ancestor = parent
        }

        return false
    }

    private static func stringAttribute(_ attribute: CFString, of element: AXUIElement) -> String? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(element, attribute, &value) == .success else {
            return nil
        }
        return value as? String
    }

    private static func isBrowser(_ identifier: String?) -> Bool {
        guard let identifier = identifier?.lowercased() else { return false }
        let browserIdentifiers = [
            "com.apple.safari",
            "com.google.chrome",
            "org.chromium.chromium",
            "com.microsoft.edgemac",
            "org.mozilla.firefox",
            "company.thebrowser.browser",
            "com.brave.browser",
            "com.operasoftware.opera"
        ]
        return browserIdentifiers.contains(where: identifier.hasPrefix)
    }
}
