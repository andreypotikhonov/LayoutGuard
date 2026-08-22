using LayoutGuard.Core;

namespace LayoutGuard.Windows;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly CorrectionEngine _engine;
    private readonly KeyboardMonitor _monitor;
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _lastCorrectionItem;
    private SettingsForm? _settingsForm;

    public TrayApplicationContext()
    {
        _settings = SettingsStore.Load();
        var resources = Path.Combine(AppContext.BaseDirectory, "Resources");
        _engine = new CorrectionEngine(resources);
        _engine.WarmUp(_settings.ToCorrectionOptions());

        _enabledItem = new ToolStripMenuItem("Исправление включено")
        {
            Checked = _settings.Enabled,
            CheckOnClick = true
        };
        _enabledItem.CheckedChanged += (_, _) =>
        {
            _settings.Enabled = _enabledItem.Checked;
            _enabledItem.Text = _settings.Enabled ? "Исправление включено" : "Исправление выключено";
            SettingsStore.Save(_settings);
        };
        _lastCorrectionItem = new ToolStripMenuItem("Исправлений пока нет") { Enabled = false };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(_lastCorrectionItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Настройки неисправных клавиш…", null, (_, _) => ShowSettings());
        menu.Items.Add("О программе", null, (_, _) => MessageBox.Show(
            "LayoutGuard для Windows 0.2.2\n\nЛокальное исправление раскладки, опечаток и пропусков неисправных клавиш.\nНабираемый текст не отправляется в сеть.",
            "LayoutGuard", MessageBoxButtons.OK, MessageBoxIcon.Information));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitThread());

        _tray = new NotifyIcon
        {
            Text = "LayoutGuard",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => ShowSettings();

        _monitor = new KeyboardMonitor(_engine, () => _settings, RecordCorrection);
        try
        {
            _monitor.Start();
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "LayoutGuard", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowSettings()
    {
        _settingsForm ??= new SettingsForm(_settings, _engine);
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void RecordCorrection(CorrectionDecision decision, bool? switchedLayout)
    {
        var switchText = switchedLayout switch
        {
            true => " · раскладка ✓",
            false => " · раскладка не переключилась",
            _ => ""
        };
        _lastCorrectionItem.Text = $"{decision.Original} → {decision.Replacement}{switchText}";
    }

    protected override void ExitThreadCore()
    {
        _monitor.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _settingsForm?.Dispose();
        base.ExitThreadCore();
    }
}
