using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.System;

namespace QuickShell.Services;

internal static class QuickShellKeyboardShortcuts
{
    public static KeyChord CreateShortcut { get; } =
        KeyChordHelpers.FromModifiers(ctrl: true, alt: false, shift: false, win: false, vkey: VirtualKey.N);

    public static KeyChord Undo { get; } =
        KeyChordHelpers.FromModifiers(ctrl: true, alt: false, shift: false, win: false, vkey: VirtualKey.Z);

    public static KeyChord Redo { get; } =
        KeyChordHelpers.FromModifiers(ctrl: true, alt: false, shift: false, win: false, vkey: VirtualKey.Y);
}
