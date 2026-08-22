using System.Runtime.InteropServices;
using System.Diagnostics;
using LayoutGuard.Core;
using LayoutGuard.Windows;

var failures = new List<string>();
var settings = new AppSettings();
var resources = Path.Combine(AppContext.BaseDirectory, "Resources");
var engine = new CorrectionEngine(resources);
var options = settings.ToCorrectionOptions();

Check(
    Marshal.SizeOf<NativeMethods.Input>() == (IntPtr.Size == 8 ? 40 : 28),
    $"Win32 INPUT ABI size ({Marshal.SizeOf<NativeMethods.Input>()})");
Check(settings.RestoreBrokenKeys, "broken-key restoration is enabled by default");
Check(settings.BrokenRussianLetters.Contains('п'), "Russian п is marked as a broken key by default");

var missingLetterDecision = engine.Decide("ривет", options);
Check(
    missingLetterDecision is
    {
        Replacement: "привет",
        Reason: CorrectionReason.MissingBrokenKey
    },
    "engine classifies ривет → привет as broken-key restoration");

var typoDecision = engine.Decide("превет", options);
Check(typoDecision?.Replacement == "привет", "engine corrects an ordinary typo превет → привет");

using (var form = new Form
{
    Text = "LayoutGuard SendInput check",
    StartPosition = FormStartPosition.CenterScreen,
    Width = 420,
    Height = 140,
    ShowInTaskbar = false
})
using (var input = new TextBox { Dock = DockStyle.Fill, Multiline = true })
{
    form.Controls.Add(input);
    form.Show();
    form.Activate();
    input.Focus();
    Application.DoEvents();

    var sent = TextInjector.ReplacePreviousText(0, "привет");
    var deadline = DateTime.UtcNow.AddSeconds(2);
    while (input.Text != "привет" && DateTime.UtcNow < deadline)
    {
        Application.DoEvents();
        Thread.Sleep(10);
    }

    Check(sent, "SendInput accepted the complete Unicode batch");
    Check(input.Text == "привет", $"SendInput delivered Unicode text (actual: '{input.Text}')");

    input.Clear();
    input.Text = "ghb";
    input.SelectionStart = input.TextLength;
    input.Focus();
    Application.DoEvents();

    var prefixReplaced = TextInjector.ReplacePreviousText(3, "прив");
    PumpUntil(() => input.Text == "прив");
    var layoutSwitched = InputLanguageSwitcher.Select(SupportedLanguage.Russian);
    var suffixSent = SendVirtualKeys(0x54, 0x4e); // physical T, N => Russian е, т
    PumpUntil(() => input.Text == "привет");

    Check(prefixReplaced, "early correction replaced the delivered source prefix");
    Check(layoutSwitched, "foreground input language switched synchronously");
    Check(suffixSent, "physical suffix keys were accepted");
    Check(input.Text == "привет", $"early ghbdtn scenario produced one corrected word (actual: '{input.Text}')");

    input.Text = "ривет";
    input.SelectionStart = input.TextLength;
    input.Focus();
    Application.DoEvents();
    var missingLetterInjected = missingLetterDecision is not null &&
        TextInjector.ReplacePreviousText(
            missingLetterDecision.Original.Length,
            missingLetterDecision.Replacement + " ");
    PumpUntil(() => input.Text == "привет ");

    Check(missingLetterInjected, "boundary correction batch for ривет was accepted");
    Check(input.Text == "привет ", $"typing ривет plus Space produced привет plus Space (actual: '{input.Text}')");

    input.Text = "превет";
    input.SelectionStart = input.TextLength;
    input.Focus();
    Application.DoEvents();
    var typoInjected = typoDecision is not null &&
        TextInjector.ReplacePreviousText(typoDecision.Original.Length, typoDecision.Replacement + " ");
    PumpUntil(() => input.Text == "привет ");

    Check(typoInjected, "ordinary typo correction batch was accepted");
    Check(input.Text == "привет ", $"typing превет plus Space produced привет plus Space (actual: '{input.Text}')");

    using var monitor = new KeyboardMonitor(engine, () => settings, (_, _) => { }, _ => false);

    input.Clear();
    FeedMonitor(monitor, input, "ривет ");
    Check(input.Text == "привет ",
        $"KeyboardMonitor corrected ривет at the word boundary (actual: '{input.Text}')");

    input.Clear();
    InputLanguageSwitcher.Select(SupportedLanguage.English);
    FeedMonitor(monitor, input, "ghbd");
    FeedMonitor(monitor, input, "ет");
    Check(input.Text == "привет",
        $"KeyboardMonitor replaced and switched the early ghbd prefix (actual: '{input.Text}')");

    ResetMonitor(monitor, input);
    InputLanguageSwitcher.Select(SupportedLanguage.Russian);
    FeedMonitor(monitor, input, "рудд");
    FeedMonitor(monitor, input, "o");
    Check(input.Text == "hello",
        $"KeyboardMonitor replaced and switched the early рудд prefix (actual: '{input.Text}')");

    ResetMonitor(monitor, input);
    FeedMonitor(monitor, input, "сейчасскаким ");
    Check(input.Text == "сейчас с каким ",
        $"KeyboardMonitor restored missing spaces (actual: '{input.Text}')");

    ResetMonitor(monitor, input);
    InputLanguageSwitcher.Select(SupportedLanguage.English);
    FeedMonitor(monitor, input, "hello ");
    Check(input.Text == "hello ",
        $"KeyboardMonitor preserved a valid word (actual: '{input.Text}')");

    using var latencyMonitor = new KeyboardMonitor(engine, () => settings, (_, _) => { }, _ => false);
    var latencyWatch = Stopwatch.StartNew();
    for (var index = 0; index < 5_000; index++)
    {
        latencyMonitor.HandleKey(new KeyStroke(0, 0, "hello"[index % 5].ToString(), false));
    }
    latencyWatch.Stop();
    var averageHandlerMilliseconds = latencyWatch.Elapsed.TotalMilliseconds / 5_000;
    Check(averageHandlerMilliseconds < 0.5,
        $"keyboard handler stays below 0.5 ms/key average (actual: {averageHandlerMilliseconds:F4} ms)");

    InputLanguageSwitcher.Select(SupportedLanguage.English);
}

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("All Windows checks passed.");
return 0;

void Check(bool condition, string name)
{
    if (condition)
    {
        Console.WriteLine($"PASS: {name}");
    }
    else
    {
        failures.Add($"FAIL: {name}");
    }
}

void PumpUntil(Func<bool> condition)
{
    var deadline = DateTime.UtcNow.AddSeconds(2);
    while (!condition() && DateTime.UtcNow < deadline)
    {
        Application.DoEvents();
        Thread.Sleep(10);
    }
}

bool SendVirtualKeys(params ushort[] virtualKeys)
{
    var events = new List<NativeMethods.Input>(virtualKeys.Length * 2);
    foreach (var virtualKey in virtualKeys)
    {
        events.Add(VirtualKey(virtualKey, 0));
        events.Add(VirtualKey(virtualKey, NativeMethods.KeyeventfKeyup));
    }
    return NativeMethods.SendInput(
        (uint)events.Count,
        events.ToArray(),
        Marshal.SizeOf<NativeMethods.Input>()) == events.Count;
}

NativeMethods.Input VirtualKey(ushort virtualKey, uint flags) => new()
{
    Type = NativeMethods.InputKeyboard,
    Data = new NativeMethods.InputUnion
    {
        Keyboard = new NativeMethods.KeyboardInput
        {
            VirtualKey = virtualKey,
            Flags = flags,
            ExtraInfo = NativeMethods.InjectionSignature
        }
    }
};

void FeedMonitor(KeyboardMonitor monitor, TextBox target, string text)
{
    foreach (var character in text)
    {
        var stroke = new KeyStroke(0, 0, character.ToString(), false);
        var suppressed = monitor.HandleKey(stroke);
        if (!suppressed)
        {
            TextInjector.ReplacePreviousText(0, character.ToString());
        }
        PumpUntil(() => target.Text.EndsWith(character.ToString()) || suppressed);
        Application.DoEvents();
    }
}

void ResetMonitor(KeyboardMonitor monitor, TextBox target)
{
    FeedMonitor(monitor, target, ".");
    target.Clear();
    Application.DoEvents();
}
