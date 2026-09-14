using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Parses key names for the dummy-player scripts into Input System keys.
/// Accepts Input System names ("U", "UpArrow", "Numpad1") and the legacy
/// KeyCode spellings still used in prefabs/scenes ("Keypad1", "Alpha1",
/// "Return", "LeftShift"...), so existing serialized values keep working.
/// </summary>
public static class KeyName
{
    public static Key Parse(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Key.None;

        if (Enum.TryParse(name, true, out Key key))
            return key;

        string alias = name;
        if (alias.StartsWith("Keypad", StringComparison.OrdinalIgnoreCase))
            alias = "Numpad" + alias.Substring("Keypad".Length);
        else if (alias.StartsWith("Alpha", StringComparison.OrdinalIgnoreCase))
            alias = "Digit" + alias.Substring("Alpha".Length);
        else
            alias = alias switch
            {
                "Return" => "Enter",
                "KeypadEnter" => "NumpadEnter",
                "KeypadPeriod" => "NumpadPeriod",
                "KeypadDivide" => "NumpadDivide",
                "KeypadMultiply" => "NumpadMultiply",
                "KeypadMinus" => "NumpadMinus",
                "KeypadPlus" => "NumpadPlus",
                "Escape" => "Escape",
                "BackQuote" => "Backquote",
                "Minus" => "Minus",
                "Equals" => "Equals",
                "LeftBracket" => "LeftBracket",
                "RightBracket" => "RightBracket",
                "Backslash" => "Backslash",
                "Semicolon" => "Semicolon",
                "Quote" => "Quote",
                "Comma" => "Comma",
                "Period" => "Period",
                "Slash" => "Slash",
                "LeftControl" => "LeftCtrl",
                "RightControl" => "RightCtrl",
                "LeftCommand" => "LeftMeta",
                "RightCommand" => "RightMeta",
                "LeftWindows" => "LeftMeta",
                "RightWindows" => "RightMeta",
                "CapsLock" => "CapsLock",
                "Numlock" => "NumLock",
                "ScrollLock" => "ScrollLock",
                "PageUp" => "PageUp",
                "PageDown" => "PageDown",
                "Print" => "PrintScreen",
                "Menu" => "ContextMenu",
                _ => alias,
            };

        if (Enum.TryParse(alias, true, out key))
            return key;

        Debug.LogWarning($"Unknown key name '{name}', using Key.None.");
        return Key.None;
    }
}
