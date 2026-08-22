import AppKit
import CoreGraphics
import Foundation

final class KeyboardMonitor {
    private struct PrefixConversion {
        let original: String
        let replacement: String
        let utf16Length: Int
    }

    private let correctionEngine = CorrectionEngine()
    private weak var model: AppModel?
    private var eventTap: CFMachPort?
    private var runLoopSource: CFRunLoopSource?
    private var currentWord = ""
    private var sentencePrefix = ""
    private var currentApplicationIdentifier: String?
    private var applicationObserver: NSObjectProtocol?
    private var secureFieldFocused = false
    private var secureFieldRefreshWorkItem: DispatchWorkItem?
    private let maximumSentenceLength = 512

    init(model: AppModel) {
        self.model = model
        currentApplicationIdentifier = NSWorkspace.shared.frontmostApplication?.bundleIdentifier
        applicationObserver = NSWorkspace.shared.notificationCenter.addObserver(
            forName: NSWorkspace.didActivateApplicationNotification,
            object: nil,
            queue: .main
        ) { [weak self] notification in
            guard let self else { return }
            let application = notification.userInfo?[NSWorkspace.applicationUserInfoKey]
                as? NSRunningApplication
            self.currentApplicationIdentifier = application?.bundleIdentifier
            self.resetInputContext()
            self.scheduleSecureFieldRefresh()
        }
    }

    deinit {
        secureFieldRefreshWorkItem?.cancel()
        if let applicationObserver {
            NSWorkspace.shared.notificationCenter.removeObserver(applicationObserver)
        }
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
        refreshSecureFieldStatus()
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
            scheduleSecureFieldRefresh()
            return Unmanaged.passUnretained(event)
        }

        model?.recordObservedKey()

        guard let model, model.isEnabled, model.hasAccessibilityPermission else {
            resetInputContext()
            return Unmanaged.passUnretained(event)
        }

        let flags = event.flags
        if flags.contains(.maskCommand) || flags.contains(.maskControl) || flags.contains(.maskAlternate) {
            resetInputContext()
            scheduleSecureFieldRefresh()
            return Unmanaged.passUnretained(event)
        }

        let keyCode = event.getIntegerValueField(.keyboardEventKeycode)
        if keyCode == 48 { // Tab can move focus into or out of a password field.
            resetInputContext()
            scheduleSecureFieldRefresh()
            return Unmanaged.passUnretained(event)
        }

        if isExcludedApplication(identifier: currentApplicationIdentifier, model: model) ||
            secureFieldFocused {
            resetInputContext()
            return Unmanaged.passUnretained(event)
        }

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

        if text.allSatisfy({ $0.isLetter || $0 == "-" || $0 == "'" }) ||
            canStartWrongLayoutWord(with: text) {
            if text.allSatisfy({ $0.isLetter }),
               LayoutConverter.needsWordBoundary(between: currentWord, and: text) {
                let previousWord = currentWord
                let decision = correctionEngine.decision(
                    for: previousWord,
                    correctTypos: model.correctTypos
                )
                let correctedPreviousWord = decision?.replacement ?? previousWord
                let replacedLength = decision == nil ? 0 : previousWord.utf16.count
                let insertedText = (decision == nil ? "" : correctedPreviousWord) + " " + text

                TextInjector.replacePreviousText(
                    utf16Length: replacedLength,
                    with: insertedText
                )
                appendToSentence(correctedPreviousWord + " ")
                currentWord = text

                if let decision {
                    let switchedLayout = decision.reason == .wrongLayout
                        ? InputSourceController.select(decision.language)
                        : nil
                    model.recordCorrection(
                        original: previousWord,
                        replacement: correctedPreviousWord,
                        switchedLayout: switchedLayout
                    )
                }
                return nil
            }

            currentWord.append(contentsOf: text)
            if currentWord.count > 64 { currentWord = "" }

            if currentWord.count >= 5,
               let decision = correctionEngine.layoutDecision(for: currentWord) {
                let original = currentWord
                let prefixConversion = safelyConvertedSentencePrefix(to: decision.language)
                let originalPhrase = prefixConversion.map { $0.original + original } ?? original
                let replacementPhrase = (prefixConversion?.replacement ?? "") +
                    decision.replacement
                let previouslyDeliveredLength = (prefixConversion?.utf16Length ?? 0) +
                    max(0, original.utf16.count - text.utf16.count)
                if let prefixConversion {
                    apply(prefixConversion)
                }
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
        let sentenceAppendText: String
        if decision.reason == .wrongLayout {
            if let prefixConversion = safelyConvertedSentencePrefix(to: decision.language) {
                original = prefixConversion.original + word
                correctedText = prefixConversion.replacement + decision.replacement
                replacedLength = prefixConversion.utf16Length + word.utf16.count
                sentenceAppendText = decision.replacement + text
                apply(prefixConversion)
            } else {
                original = word
                correctedText = decision.replacement
                replacedLength = word.utf16.count
                sentenceAppendText = correctedText + text
            }
        } else {
            original = word
            correctedText = decision.replacement
            replacedLength = word.utf16.count
            sentenceAppendText = correctedText + text
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
        appendToSentence(sentenceAppendText)

        return nil
    }

    private func safelyConvertedSentencePrefix(
        to language: SupportedLanguage
    ) -> PrefixConversion? {
        let prefix = sentencePrefix as NSString
        let fullRange = NSRange(location: 0, length: prefix.length)
        guard let expression = try? NSRegularExpression(pattern: "\\p{L}+") else {
            return nil
        }
        let matches = expression.matches(in: sentencePrefix, range: fullRange)
        guard !matches.isEmpty else { return nil }

        var suffixLocation = prefix.length
        let sourceLanguage: SupportedLanguage = language == .russian ? .english : .russian
        for match in matches.reversed() {
            let string = prefix.substring(with: match.range)
            guard LayoutConverter.language(of: string) == sourceLanguage,
                  let converted = LayoutConverter.convert(string, to: language) else {
                break
            }
            guard correctionEngine.isCorrectlySpelled(converted, language: language) else {
                break
            }
            suffixLocation = match.range.location
        }

        guard suffixLocation < prefix.length else { return nil }
        let original = prefix.substring(from: suffixLocation)
        guard let replacement = LayoutConverter.convert(original, to: language) else {
            return nil
        }
        return PrefixConversion(
            original: original,
            replacement: replacement,
            utf16Length: prefix.length - suffixLocation
        )
    }

    private func apply(_ conversion: PrefixConversion) {
        let prefix = sentencePrefix as NSString
        let untouchedLength = prefix.length - conversion.utf16Length
        sentencePrefix = prefix.substring(to: untouchedLength) + conversion.replacement
    }

    private func canStartWrongLayoutWord(with text: String) -> Bool {
        guard currentWord.isEmpty, text.count == 1 else { return false }
        return "[];,.'".contains(text)
    }

    private func appendToSentence(_ text: String) {
        if isSentenceBoundary(text) {
            sentencePrefix = ""
            return
        }

        sentencePrefix.append(contentsOf: text)

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

    private func scheduleSecureFieldRefresh() {
        secureFieldRefreshWorkItem?.cancel()
        let workItem = DispatchWorkItem { [weak self] in
            self?.refreshSecureFieldStatus()
        }
        secureFieldRefreshWorkItem = workItem
        // Focus is updated just after the mouse/shortcut event reaches the app.
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.04, execute: workItem)
    }

    private func refreshSecureFieldStatus() {
        secureFieldFocused = SecureFieldDetector.isSecureFieldFocused()
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
