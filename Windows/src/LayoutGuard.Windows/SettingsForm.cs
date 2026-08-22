using LayoutGuard.Core;

namespace LayoutGuard.Windows;

internal sealed class SettingsForm : Form
{
    private const string RussianAlphabet = "абвгдеёжзийклмнопрстуфхцчшщъыьэюя";
    private readonly AppSettings _settings;
    private readonly CorrectionEngine _engine;
    private readonly Dictionary<char, CheckBox> _letterBoxes = [];
    private readonly NumericUpDown _maximumMissing = new();
    private readonly CheckBox _restore = new();
    private readonly CheckBox _typos = new();
    private readonly CheckBox _spaces = new();
    private readonly CheckBox _startWithWindows = new();
    private readonly TextBox _customWords = new();
    private readonly TextBox _testInput = new();
    private readonly Label _testResult = new();

    public event Action? SettingsChanged;

    public SettingsForm(AppSettings settings, CorrectionEngine engine)
    {
        _settings = settings;
        _engine = engine;
        Text = "LayoutGuard — настройки";
        Width = 620;
        Height = 690;
        MinimumSize = new Size(560, 620);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10);
        BuildInterface();
    }

    private void BuildInterface()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 1,
            AutoScroll = true
        };
        Controls.Add(root);

        root.Controls.Add(Heading("Неисправные клавиши"));
        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(540, 0),
            Text = "Отметьте русские буквы, клавиши которых иногда не срабатывают. " +
                   "LayoutGuard сможет восстановить до трёх таких пропусков в одном слове."
        });

        var letters = new FlowLayoutPanel
        {
            AutoSize = true,
            MaximumSize = new Size(550, 0),
            WrapContents = true,
            Padding = new Padding(0, 8, 0, 8)
        };
        foreach (var letter in RussianAlphabet)
        {
            var box = new CheckBox
            {
                Appearance = Appearance.Button,
                AutoSize = true,
                Text = letter.ToString(),
                TextAlign = ContentAlignment.MiddleCenter,
                Checked = _settings.BrokenRussianLetters.Contains(letter)
            };
            _letterBoxes[letter] = box;
            letters.Controls.Add(box);
        }
        root.Controls.Add(letters);

        _restore.AutoSize = true;
        _restore.Text = "Восстанавливать пропуски неисправных клавиш";
        _restore.Checked = _settings.RestoreBrokenKeys;
        root.Controls.Add(_restore);

        var missingPanel = new FlowLayoutPanel { AutoSize = true };
        missingPanel.Controls.Add(new Label { AutoSize = true, Text = "Максимум пропущенных букв в слове:", Padding = new Padding(0, 7, 8, 0) });
        _maximumMissing.Minimum = 1;
        _maximumMissing.Maximum = 3;
        _maximumMissing.Value = Math.Clamp(_settings.MaximumMissingLetters, 1, 3);
        _maximumMissing.Width = 55;
        missingPanel.Controls.Add(_maximumMissing);
        root.Controls.Add(missingPanel);

        _typos.AutoSize = true;
        _typos.Text = "Исправлять обычные опечатки";
        _typos.Checked = _settings.CorrectTypos;
        root.Controls.Add(_typos);

        _spaces.AutoSize = true;
        _spaces.Text = "Восстанавливать пропущенные пробелы между словами";
        _spaces.Checked = _settings.CorrectMissingSpaces;
        root.Controls.Add(_spaces);

        root.Controls.Add(Heading("Проверка"));
        var testPanel = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
        _testInput.Width = 280;
        _testInput.PlaceholderText = "Например: ивет или ghbdtn";
        testPanel.Controls.Add(_testInput);
        var testButton = new Button { Text = "Проверить", AutoSize = true };
        testButton.Click += (_, _) => TestCorrection();
        testPanel.Controls.Add(testButton);
        root.Controls.Add(testPanel);
        _testResult.AutoSize = true;
        _testResult.ForeColor = Color.FromArgb(40, 90, 170);
        root.Controls.Add(_testResult);

        root.Controls.Add(Heading("Личный словарь"));
        root.Controls.Add(new Label { AutoSize = true, Text = "По одному слову в строке — эти слова никогда не будут исправляться." });
        _customWords.Multiline = true;
        _customWords.ScrollBars = ScrollBars.Vertical;
        _customWords.Height = 90;
        _customWords.Dock = DockStyle.Top;
        _customWords.Text = string.Join(Environment.NewLine, _settings.CustomWords);
        root.Controls.Add(_customWords);

        _startWithWindows.AutoSize = true;
        _startWithWindows.Text = "Запускать вместе с Windows";
        _startWithWindows.Checked = _settings.StartWithWindows;
        root.Controls.Add(_startWithWindows);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Top,
            Padding = new Padding(0, 16, 0, 0)
        };
        var save = new Button { Text = "Сохранить", AutoSize = true };
        save.Click += (_, _) => SaveSettings();
        var cancel = new Button { Text = "Закрыть", AutoSize = true };
        cancel.Click += (_, _) => Hide();
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        root.Controls.Add(buttons);
    }

    private void TestCorrection()
    {
        ApplyControls();
        var input = _testInput.Text.Trim();
        var decision = _engine.Decide(input, _settings.ToCorrectionOptions());
        _testResult.Text = decision is null ? "Слово останется без изменений" : $"{input} → {decision.Replacement}";
    }

    private void SaveSettings()
    {
        ApplyControls();
        SettingsStore.Save(_settings);
        AutoStartManager.SetEnabled(_settings.StartWithWindows);
        _engine.WarmUp(_settings.ToCorrectionOptions());
        SettingsChanged?.Invoke();
        Hide();
    }

    private void ApplyControls()
    {
        _settings.BrokenRussianLetters = new string(_letterBoxes
            .Where(pair => pair.Value.Checked).Select(pair => pair.Key).ToArray());
        _settings.BrokenEnglishLetters = KeyboardLayoutConverter.Convert(
            _settings.BrokenRussianLetters, SupportedLanguage.English) ?? "";
        _settings.MaximumMissingLetters = (int)_maximumMissing.Value;
        _settings.RestoreBrokenKeys = _restore.Checked;
        _settings.CorrectTypos = _typos.Checked;
        _settings.CorrectMissingSpaces = _spaces.Checked;
        _settings.StartWithWindows = _startWithWindows.Checked;
        _settings.CustomWords = _customWords.Lines.Select(word => word.Trim())
            .Where(word => word.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static Label Heading(string text) => new()
    {
        AutoSize = true,
        Font = new Font("Segoe UI Semibold", 14),
        Padding = new Padding(0, 14, 0, 5),
        Text = text
    };

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnFormClosing(e);
    }
}
