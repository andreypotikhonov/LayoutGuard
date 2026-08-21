import SwiftUI

struct SettingsView: View {
    @ObservedObject var model: AppModel

    var body: some View {
        Form {
            Section("Основные функции") {
                Toggle("Автоматически исправлять раскладку", isOn: $model.isEnabled)
                Toggle("Исправлять явные опечатки", isOn: $model.correctTypos)
            }

            Section("Доступ к клавиатуре") {
                HStack {
                    Image(systemName: model.hasAccessibilityPermission ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                        .foregroundStyle(model.hasAccessibilityPermission ? .green : .orange)
                    Text(model.hasAccessibilityPermission ? "Разрешение получено" : "Требуется разрешение Accessibility")
                }

                if !model.hasAccessibilityPermission {
                    HStack {
                        Button("Запросить разрешение") { model.requestAccessibilityPermission() }
                        Button("Открыть настройки") { model.openAccessibilitySettings() }
                    }
                }
            }

            Section("Статистика") {
                LabeledContent("Исправлено слов", value: "\(model.correctionCount)")
                if let lastCorrection = model.lastCorrection {
                    LabeledContent("Последнее", value: lastCorrection)
                }
            }

            Section {
                Text("Текст обрабатывается локально. LayoutGuard хранит только текущее слово и не использует сеть.")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            }
        }
        .formStyle(.grouped)
        .frame(width: 470, height: 390)
        .padding()
    }
}
