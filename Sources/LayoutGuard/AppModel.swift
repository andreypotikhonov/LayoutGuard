import AppKit
import Combine
import Foundation

final class AppModel: ObservableObject {
    static let shared = AppModel()

    @Published var isEnabled: Bool {
        didSet {
            UserDefaults.standard.set(isEnabled, forKey: Keys.enabled)
            updateMonitoring()
        }
    }

    @Published var correctTypos: Bool {
        didSet { UserDefaults.standard.set(correctTypos, forKey: Keys.correctTypos) }
    }

    @Published private(set) var hasAccessibilityPermission = false
    @Published private(set) var correctionCount = 0
    @Published private(set) var lastCorrection: String?

    let excludedBundleIdentifiers: Set<String> = [
        "com.1password.1password",
        "com.bitwarden.desktop",
        "com.apple.keychainaccess"
    ]

    private lazy var keyboardMonitor = KeyboardMonitor(model: self)

    private enum Keys {
        static let enabled = "isEnabled"
        static let correctTypos = "correctTypos"
        static let correctionCount = "correctionCount"
    }

    private init() {
        let defaults = UserDefaults.standard
        if defaults.object(forKey: Keys.enabled) == nil {
            defaults.set(true, forKey: Keys.enabled)
        }
        if defaults.object(forKey: Keys.correctTypos) == nil {
            defaults.set(true, forKey: Keys.correctTypos)
        }

        isEnabled = defaults.bool(forKey: Keys.enabled)
        correctTypos = defaults.bool(forKey: Keys.correctTypos)
        correctionCount = defaults.integer(forKey: Keys.correctionCount)
    }

    func start() {
        refreshPermission()
        if !hasAccessibilityPermission {
            _ = AccessibilityPermission.request()
        }
        updateMonitoring()
    }

    func requestAccessibilityPermission() {
        _ = AccessibilityPermission.request()
        refreshPermission()
    }

    func refreshPermission() {
        hasAccessibilityPermission = AccessibilityPermission.isGranted
        updateMonitoring()
    }

    func openAccessibilitySettings() {
        let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility")!
        NSWorkspace.shared.open(url)
    }

    func recordCorrection(original: String, replacement: String, switchedLayout: Bool?) {
        correctionCount += 1
        UserDefaults.standard.set(correctionCount, forKey: Keys.correctionCount)
        let switchStatus: String
        switch switchedLayout {
        case true?: switchStatus = " · раскладка ✓"
        case false?: switchStatus = " · раскладка не переключилась"
        case nil: switchStatus = ""
        }
        lastCorrection = "\(original) → \(replacement)\(switchStatus)"
    }

    private func updateMonitoring() {
        guard isEnabled, hasAccessibilityPermission else {
            keyboardMonitor.stop()
            return
        }
        _ = keyboardMonitor.start()
    }
}
