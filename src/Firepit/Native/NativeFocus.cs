using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Firepit.Native;

/// <summary>
/// Thin Win32 wrapper for OS-level keyboard focus. WPF's Focus / Keyboard.Focus
/// only move the *logical* focus inside a HWND — they can't pull the OS focus
/// out of a sibling HWND that lives inside the same window. The project-picker
/// popup runs into this: WebView2 (the embedded terminal) keeps the OS focus
/// even after the popup opens, so typed characters end up in the running
/// Claude session instead of the search box.
///
/// The fix is to call <see cref="SetFocus"/> on the popup's HWND (popups with
/// AllowsTransparency="True" are real HWNDs, reachable via HwndSource) so the
/// OS routes WM_KEYDOWN there, and then set WPF Keyboard.Focus on the actual
/// TextBox inside.
/// </summary>
internal static class NativeFocus
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetFocus(IntPtr hWnd);

    /// <summary>
    /// Move OS keyboard focus to the HWND that hosts <paramref name="visual"/>.
    /// No-op (returns false) if the visual isn't yet attached to an HwndSource
    /// — caller should re-try on a later dispatcher tick if so.
    /// </summary>
    public static bool MoveOsFocusTo(Visual visual)
    {
        if (PresentationSource.FromVisual(visual) is not HwndSource src) return false;
        SetFocus(src.Handle);
        return true;
    }
}
