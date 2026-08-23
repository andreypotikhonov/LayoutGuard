# Сторонние компоненты

LayoutGuard распространяет или использует следующие компоненты:

- **WeCantSpell.Hunspell 7.0.1** — MPL-2.0. Исходный проект: <https://github.com/aarondandy/WeCantSpell.Hunspell>.
- **Interop.UIAutomationClient 10.19041.0** — MIT. Используется только для определения защищённых полей ввода.
- **Russian LibreOffice Extension Dictionary (modern)** — MPL-2.0, авторы и история указаны в поставляемом словаре.
- **SCOWL / en_US Hunspell dictionary** — лицензия и атрибуция приведены в `Resources/Licenses/EnglishDictionary-README.txt`.
- **FrequencyWords** — исходный код репозитория MIT; частотные данные OpenSubtitles 2016 предоставлены на условиях CC BY-SA 4.0. Автор набора: Hermit Dave. Источник: <https://github.com/hermitdave/FrequencyWords>.
- **OpenCorpora** — морфологический словарь и размеченный русский корпус, CC BY-SA 3.0. Из них на этапе сборки создаются компактный словарь поверхностных форм и локальные unigram/bigram/trigram-статистики. Исходные XML-корпусы в установщик не входят. Источник: <https://opencorpora.org/>.
- **pymorphy3 / pymorphy3-dicts-ru** — MIT-код используется только на этапе сборки для чтения скомпилированного словаря OpenCorpora; Python-пакеты в приложение не входят. Источник: <https://github.com/no-plagiarism/pymorphy3>.

Полные тексты применимых лицензий и уведомлений копируются в папку `Resources/Licenses` готового приложения. Код самого LayoutGuard лицензирован отдельно по MIT.
