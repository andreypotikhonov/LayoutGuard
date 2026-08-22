import AppKit
import CoreGraphics
import Foundation

final class KeyboardMonitor {
    private let correctionEngine = CorrectionEngine()
    private weak var model: AppModel?
    private var eventTap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?
    private var currentWord = ""
    private var sentencePrefix = ""
    private var currentApplicationIdentifier: String?
    private let maximumSentenceLength = 512

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
        resetInputContext()
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
            resetInputContext()
            return Unmanaged.passUnretained(event)
        }

        model?.recordObservedKey()

        guard let model, model.isEnabled, model.hasAccessibilityPermission else {
            resetInputContext()
            return Unmanaged.passUnretained(event)
        }

        let applicationIdentifier = NSWorkspace.shared.frontmostApplication?.bundleIdentifier
        if applicationIdentifier != currentApplicationIdentifier {
            resetInputContext()
            currentApplicationIdentifier = applicationIdentifier
        }

        if isExcludedApplication(identifier: applicationIdentifier, model: model) ||
            SecureFieldDetector.isSecureFieldFocused() {
            resetInputContext()
            return Unmanaged.passUnretained(event)
        }

        let flags = event.flags
        if flags.contains(.maskCommand) || flags.contains(.maskControl) || flags.contains(.maskAlternate) {
            resetInputContext()
            return Unmanaged.passUnretained(event)
        }

        let keyCode = event.getIntegerValueField(.keyboardEventKeycode)
        if keyCode == 51 {
            if !currentWord.isEmpty {
                currentWord.removeLast()
            } else if !sentencePrefix.isEmpty {
                sentencePrefix.removeLast()
            }
            return Unmanaged.passUnretained(event)
        }

        guard let text = unicodeString(from: event), !text.isEmpty else {
            resetInputContext()
            return Unmanaged.passUnretained(event)
        }

        if text.allSatisfy({ $0.isLetter || $0 == "-" || $0 == "'" }) {
            currentWord.append(contentsOf: text)
            if currentWord.count > 64 { currentWord = "" }

            if currentWord.count >= 5,
               let decision = correctionEngine.layoutDecision(for: currentWord) {
                let original = currentWord
                let originalPhrase = sentencePrefix + original
                let convertedPrefix = convertedSentencePrefix(to: decision.language)
                let replacementPhrase = convertedPrefix + decision.replacement
                let previouslyDeliveredLength = sentencePrefix.utf16.count +
                    max(0, original.utf16.count - text.utf16.count)
                sentencePrefix = convertedPrefix
                currentWord = decision.replacement

                TextInjector.replacePreviousText(
                    utf16Length: previouslyDeliveredLength,
                    with: replacementPhrase
                )
                let switchedLayout = InputSourceController.select(decision.language)
                model.recordCorrection(
                    original: originalPhrase,
                    replacement: replacementPhrase,
                    switchedLayout: switchedLayout
                )

                return nil
            }

            return Unmanaged.passUnretained(event)
        }

        guard !currentWord.isEmpty else {
            appendToSentence(text)
            return Unmanaged.passUnretained(event)
        }
        let word = currentWord
        currentWord = ""

        guard let decision = correctionEngine.decision(
            for: word,
            correctTypos: model.correctTypos
        ) else {
            appendToSentence(word + text)
            return Unmanaged.passUnretained(event)
        }

        let original: String
        let correctedText: String
        let replacedLength: Int
        if decision.reason == .wrongLayout {
            let convertedPrefix = convertedSentencePrefix(to: decision.language)
            original = sentencePrefix + word
            correctedText = convertedPrefix + decision.replacement
            replacedLength = sentencePrefix.utf16.count + word.utf16.count
        } else {
            original = word
            correctedText = decision.replacement
            replacedLength = word.utf16.count
        }

        TextInjector.replacePreviousText(
            utf16Length: replacedLength,
            with: correctedText + text
        )
        let switchedLayout = decision.reason == .wrongLayout
            ? InputSourceController.select(decision.language)
            : nil
        model.recordCorrection(
            original: original,
            replacement: correctedText,
            switchedLayout: switchedLayout
        )
        appendToSentence(correctedText + text, replacingCurrentPrefix: decision.reason == .wrongLayout)

        return nil
    }

    private func convertedSentencePrefix(to language: SupportedLanguage) -> String {
        LayoutConverter.convert(sentencePrefix, to: language) ?? sentencePrefix
    }

    private func appendToSentence(_ text: String, replacingCurrentPrefix: Bool = false) {
        if isSentenceBoundary(text) {
            sentencePrefix = ""
            return
        }

        if replacingCurrentPrefix {
            sentencePrefix = text
        } else {
            sentencePrefix.append(contentsOf: text)
        }

        if sentencePrefix.count > maximumSentenceLength {
            sentencePrefix = ""
        }
    }

    private func isSentenceBoundary(_ text: String) -> Bool {
        text.contains(where: { character in
            character == "." || character == "!" || character == "?" || character.isNewline
        })
    }

    private func resetInputContext() {
        currentWord = ""
        sentencePrefix = ""
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
