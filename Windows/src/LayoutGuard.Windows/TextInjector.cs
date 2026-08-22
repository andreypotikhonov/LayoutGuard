using System.Runtime.InteropServices;

namespace LayoutGuard.Windows;

internal static class TextInjector
{
    public static bool ReplacePreviousText(int utf16Length, string replacement)
    {
        var inputs = new List<NativeMethods.Input>(utf16Length * 2 + replacement.Length * 2);
        for (var index = 0; index < utf16Length; index++)
        {
            inputs.Add(Key(0x08, 0, 0));
            inputs.Add(Key(0x08, 0, NativeMethods.KeyeventfKeyup));
        }
        foreach (var unit in replacement)
        {
            inputs.Add(Key(0, unit, NativeMethods.KeyeventfUnicode));
            inputs.Add(Key(0, unit, NativeMethods.KeyeventfUnicode | NativeMethods.KeyeventfKeyup));
        }
        if (inputs.Count == 0) return true;

        var sent = NativeMethods.SendInput(
            (uint)inputs.Count,
            inputs.ToArray(),
            Marshal.SizeOf<NativeMethods.Input>());
        return sent == inputs.Count;
    }

    private static NativeMethods.Input Key(ushort virtualKey, ushort scanCode, uint flags) => new()
    {
        Type = NativeMethods.InputKeyboard,
        Data = new NativeMethods.InputUnion
        {
            Keyboard = new NativeMethods.KeyboardInput
            {
                VirtualKey = virtualKey,
                ScanCode = scanCode,
                Flags = flags,
                ExtraInfo = NativeMethods.InjectionSignature
            }
        }
    };
}
