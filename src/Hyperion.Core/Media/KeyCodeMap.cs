namespace Hyperion.Core.Media;

/// <summary>Maps Windows virtual-key codes to DOM <c>KeyboardEvent.code</c> values and builds canonical combo strings.</summary>
public static class KeyCodeMap
{
    public static string? FromVirtualKey(int vk) => vk switch
    {
        >= 0x41 and <= 0x5A => "Key" + (char)vk,
        >= 0x30 and <= 0x39 => "Digit" + (char)vk,
        >= 0x70 and <= 0x7B => "F" + (vk - 0x6F),
        0x25 => "ArrowLeft",
        0x26 => "ArrowUp",
        0x27 => "ArrowRight",
        0x28 => "ArrowDown",
        0x20 => "Space",
        0xDB => "BracketLeft",
        0xDD => "BracketRight",
        0xBC => "Comma",
        0xBE => "Period",
        0xBD => "Minus",
        0xBB => "Equal",
        0xBA => "Semicolon",
        0xDE => "Quote",
        0xBF => "Slash",
        0xDC => "Backslash",
        0xC0 => "Backquote",
        _ => null,
    };

    /// <summary>Canonical modifier order: Ctrl+Alt+Shift+Meta+code. The injected script builds the same string.</summary>
    public static string Combo(bool ctrl, bool alt, bool shift, bool meta, string code) =>
        (ctrl ? "Ctrl+" : "") + (alt ? "Alt+" : "") + (shift ? "Shift+" : "") + (meta ? "Meta+" : "") + code;
}
