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
Check(settings.BrokenRussianLetters.Contains('р'), "Russian р is marked as a broken key by default");
Check(settings.BrokenRussianLetters.Contains('э'), "Russian э is marked as a broken key by default");
Check(!settings.BrokenRussianLetters.Contains('и'), "Russian и is not marked as a broken key");
Check(settings.BrokenEnglishLetters == "gh'", "physical English keys match Russian прэ");
Check(!settings.CorrectTypos, "general spelling replacement is disabled by default");
Check(!settings.CorrectMissingSpaces, "speculative missing-space correction is disabled by default");

var missingLetterDecision = engine.Decide("ривет", options);
Check(
    missingLetterDecision is
    {
        Replacement: "привет",
        Reason: CorrectionReason.MissingBrokenKey
    },
    "engine classifies ривет → привет as broken-key restoration");

Check(engine.Decide("кзамен", options)?.Replacement == "экзамен",
    "trained model restores missing Russian э");
Check(engine.Decide("лектрон", options)?.Replacement == "электрон",
    "trained model restores missing Russian э in a longer word");
Check(engine.Decide("кран", options) is null,
    "trained model does not rewrite valid кран as экран");
Check(engine.Decide("потести", options) is null,
    "trained model preserves unknown потести");
Check(engine.Decide("релизь", options) is null,
    "trained model preserves unknown релизь");
Check(engine.Decide("превет", options) is null,
    "trained model does not turn an ordinary typo into a different word");

var modelWatch = Stopwatch.StartNew();
for (var index = 0; index < 2_000; index++)
{
    _ = engine.Decide(index % 2 == 0 ? "кзамен" : "ривет", options);
}
modelWatch.Stop();
var averageModelMilliseconds = modelWatch.Elapsed.TotalMilliseconds / 2_000;
Check(averageModelMilliseconds < 2,
    $"trained word model stays below 2 ms/word average (actual: {averageModelMilliseconds:F4} ms)");

var typoDecision = engine.Decide("превет", new CorrectionOptions { CorrectTypos = true });
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

    Check(NativeMethods.GetFocusedInputWindow() == input.Handle,
        "focused child input window is used instead of only the top-level window");

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
    FeedMonitor(monitor, input, "потести ");
    Check(input.Text == "потести ",
        $"KeyboardMonitor did not split потести into syllables (actual: '{input.Text}')");

    ResetMonitor(monitor, input);
    InputLanguageSwitcher.Select(SupportedLanguage.Russian);
    FeedMonitor(monitor, input, "релизь ");
    Check(input.Text == "релизь ",
        $"KeyboardMonitor preserved релизь without switching or rewriting (actual: '{input.Text}')");

    ResetMonitor(monitor, input);
    FeedMonitor(monitor, input, "сетифика ");
    Check(!input.Text.TrimEnd().Contains(' '),
        $"KeyboardMonitor did not insert spaces inside сетифика (actual: '{input.Text}')");

    var certificateSettings = new AppSettings
    {
        CorrectTypos = false,
        CorrectMissingSpaces = false,
        BrokenRussianLetters = "рт",
        MaximumMissingLetters = 3
    };
    using var certificateMonitor = new KeyboardMonitor(
        engine, () => certificateSettings, (_, _) => { }, _ => false);
    input.Clear();
    FeedMonitor(certificateMonitor, input, "сетифика ");
    Check(input.Text == "сертификат ",
        $"KeyboardMonitor restored configured broken р and т keys (actual: '{input.Text}')");

    var spaceSettings = new AppSettings { CorrectMissingSpaces = true };
    using var spaceMonitor = new KeyboardMonitor(engine, () => spaceSettings, (_, _) => { }, _ => false);
    input.Clear();
    FeedMonitor(spaceMonitor, input, "сейчасскаким ");
    Check(input.Text == "сейчас с каким ",
        $"KeyboardMonitor restored a high-confidence missing-space phrase when enabled (actual: '{input.Text}')");

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
