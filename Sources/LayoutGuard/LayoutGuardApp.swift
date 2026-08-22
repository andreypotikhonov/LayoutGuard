import AppKit
import SwiftUI

final class AppDelegate: NSObject, NSApplicationDelegate {
    func applicationDidFinishLaunching(_ notification: Notification) {
        AppModel.shared.start()
    }

    func applicationDidBecomeActive(_ notification: Notification) {
        AppModel.shared.refreshPermission()
    }
}

@main
struct LayoutGuardApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) private var appDelegate
    @StateObject private var model = AppModel.shared

    var body: some Scene {
        MenuBarExtra {
            Toggle("LayoutGuard включён", isOn: $model.isEnabled)
            Toggle("Исправлять опечатки", isOn: $model.correctTypos)

            Divider()

            if model.hasAccessibilityPermission {
                Label("Доступ к клавиатуре разрешён", systemImage: "checkmark.shield")
            } else {
                Button("Разрешить доступ к клавиатуре…") {
                    model.requestAccessibilityPermission()
                    model.openAccessibilitySettings()
                }
            }

            if model.hasAccessibilityPermission {
                Label(
                    model.inputMonitorActive ? "Перехват ввода активен" : "Перехват ввода не запущен",
                    systemImage: model.inputMonitorActive ? "waveform.path.ecg" : "exclamationmark.triangle"
                )
                Text("Получено клавиш: \(model.observedKeyCount)")
                    .foregroundStyle(.secondary)
            }

            if let correction = model.lastCorrection {
                Text(correction)
                    .foregroundStyle(.secondary)
            }

            Divider()

            Button("Настройки…") {
                NSApplication.shared.sendAction(
                    Selector(("showSettingsWindow:")),
                    to: nil,
                    from: nil
                )
            }

            Button("Завершить LayoutGuard") {
                NSApplication.shared.terminate(nil)
            }
        } label: {
            Image(systemName: model.isEnabled ? "keyboard.badge.ellipsis" : "keyboard")
        }

        Settings {
            SettingsView(model: model)
        }
    }
}
