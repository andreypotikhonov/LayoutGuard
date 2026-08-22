import AppKit
import CoreGraphics
import Foundation

final class KeyboardMonitor {
    private let correctionEngine = CorrectionEngine()
    private weak var model: AppModel?
    private var eventTap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?
    private var currentWord = ""
    private var currentApplicationIdentifier: String?

    init(model: AppModel) {
        self.model = model
    }

    func start() -> Bool {
        guard eventTap == nil else { return true }

        let mask = CGEventMask(
            (1 << CGEventType.keyDown.rawValue) |
            (1 << CGEventType.leftMouseDown.rawValue) |
            (1 << CGEventType.rightMouseDown.rawValue) |
            (1 << CGEventType.otherMouseDown.rawValue)
        )
        let pointer = Unmanaged.passUnretained(self).toOpaque()

        guard let tap = CGEvent.tapCreate(
            tap: .cgSessionEventTap,
            place: .headInsertEventTap,
            options: .defaultTap,
            eventsOfInterest: mask,
            callback: { _, type, event, userInfo in
                guard let userInfo else { return Unmanaged.passUnretained(event) }
                let monitor = Unmanaged<KeyboardMonitor>.fromOpaque(userInfo).takeUnretainedValue()
                return monitor.handle(type: type, event: event)
            },
            userInfo: pointer
        ) else {
            return false
        }

        eventTap = tap
        let source = CFMachPortCreateRunLoopSource(kCFAllocatorDefault, tap, 0)
        runLoopSource = source
        CFRunLoopAddSource(CFRunLoopGetMain(), source, .commonModes)
        CGEvent.tapEnable(tap: tap, enable: true)
        return true
    }

    func stop() {
        if let eventTap {
            CGEvent.tapEnable(tap: eventTap, enable: false)
        }
        if let runLoopSource {
            CFRunLoopRemoveSource(CFRunLoopGetMain(), runLoopSource, .commonModes)
        }
        eventTap = nil
        runLoopSource = nil
        currentWord = ""
    }

    private func handle(type: CGEventType, event: CGEvent) -> Unmanaged<CGEvent>? {
        if type == .tapDisabledByTimeout || type == .tapDisabledByUserInput {
            if let eventTap { CGEvent.tapEnable(tap: eventTap, enable: true) }
            return Unmanaged.passUnretained(event)
        }

        if event.getIntegerValueField(.eventSourceUserData) == TextInjector.eventSignature {
            return Unmanaged.passUnretained(event)
        }

        guard type == .keyDown else {
            currentWord = ""
            return Unmanaged.passUnretained(event)
        }

        model?.recordObservedKey()

        guard let model, model.isEnabled, model.hasAccessibilityPermission else {
            currentWord = ""
            return Unmanaged.passUnretained(event)
        }

        let applicationIdentifier = NSWorkspace.shared.frontmostApplication?.bundleIdentifier
        if applicationIdentifier != currentApplicationIdentifier {
            currentWord = ""
            currentApplicationIdentifier = applicationIdentifier
        }

        if isExcludedApplication(identifier: applicationIdentifier, model: model) ||
            SecureFieldDetector.isSecureFieldFocused() {
            currentWord = ""
            return Unmanaged.passUnretained(event)
        }

        let flags = event.flags
        if flags.contains(.maskCommand) || flags.contains(.maskControl) || flags.contains(.maskAlternate) {
            currentWord = ""
            return Unmanaged.passUnretained(event)
        }

        let keyCode = event.getIntegerValueField(.keyboardEventKeycode)
        if keyCode == 51 {
            if !currentWord.isEmpty { currentWord.removeLast() }
            return Unmanaged.passUnretained(event)
        }

        guard let text = unicodeString(from: event), !text.isEmpty else {
            currentWord = ""
            return Unmanaged.passUnretained(event)
        }

        if text.allSatisfy({ $0.isLetter || $0 == "-" || $0 == "'" }) {
            currentWord.append(contentsOf: text)
            if currentWord.count > 64 { currentWord = "" }

            if currentWord.count >= 5,
               let decision = correctionEngine.layoutDecision(for: currentWord) {
                let original = currentWord
                let previouslyDeliveredLength = max(0, original.utf16.count - text.utf16.count)
                currentWord = decision.replacement

                TextInjector.replacePreviousText(
                    utf16Length: previouslyDeliveredLength,
                    with: decision.replacement
                )
                let switchedLayout = InputSourceController.select(decision.language)
                model.recordCorrection(
                    original: original,
                    replacement: decision.replacement,
                    switchedLayout: switchedLayout
                )

                return nil
            }

            return Unmanaged.passUnretained(event)
        }

        guard !currentWord.isEmpty else { return Unmanaged.passUnretained(event) }
        let word = currentWord
        currentWord = ""

        guard let decision = correctionEngine.decision(
            for: word,
            correctTypos: model.correctTypos
        ) else {
            return Unmanaged.passUnretained(event)
        }

        let replacement = decision.replacement + text
        TextInjector.replacePreviousText(utf16Length: word.utf16.count, with: replacement)
        let switchedLayout = decision.reason == .wrongLayout
            ? InputSourceController.select(decision.language)
            : nil
        model.recordCorrection(
            original: word,
            replacement: decision.replacement,
            switchedLayout: switchedLayout
        )

        return nil
    }

    private func unicodeString(from event: CGEvent) -> String? {
        var length = 0
        var buffer = [UniChar](repeating: 0, count: 8)
        event.keyboardGetUnicodeString(
            maxStringLength: buffer.count,
            actualStringLength: &length,
            unicodeString: &buffer
        )
        guard length > 0 else { return nil }
        return String(utf16CodeUnits: buffer, count: length)
    }

    private func isExcludedApplication(identifier: String?, model: AppModel) -> Bool {
        guard let identifier else { return false }
        return model.excludedBundleIdentifiers.contains(identifier)
    }
}
