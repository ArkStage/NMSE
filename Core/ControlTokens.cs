using System.Text.RegularExpressions;
using NMSE.Data;
using NMSE.IO;

namespace NMSE.Core;

/// <summary>
/// Input scheme used when resolving game control tokens in item text.
/// </summary>
internal enum ControlScheme
{
    /// <summary>PC keyboard and mouse (game default on Steam, GOG and unknown platforms).</summary>
    KeyboardMouse,

    /// <summary>Xbox gamepad labels (A/B/X/Y, View, Menu, L3/R3).</summary>
    Xbox,

    /// <summary>PlayStation gamepad labels (Cross/Circle/Square/Triangle, Options, Touchpad).</summary>
    PlayStation,

    /// <summary>Nintendo Switch gamepad labels (A/B/X/Y, Minus, Plus).</summary>
    Switch
}

/// <summary>
/// Replaces game control tokens (for example <c>FE_ALT1</c>) in game-sourced text with
/// readable key labels for the active input scheme. Tokens with no binding for the active
/// scheme are left untouched. Mouse button and touch labels come from the UI localisation
/// layer, with English abbreviations as the fallback.
/// </summary>
internal static class ControlTokens
{
    private static readonly Regex TokenRegex = new(@"\bFE_[A-Z0-9_]+\b", RegexOptions.Compiled);

    private static ControlScheme _scheme = ControlScheme.KeyboardMouse;

    /// <summary>Sets the input scheme used by <see cref="Resolve"/>.</summary>
    internal static void SetScheme(ControlScheme scheme) => _scheme = scheme;

    /// <summary>Gets the input scheme currently used by <see cref="Resolve"/>.</summary>
    internal static ControlScheme GetScheme() => _scheme;

    /// <summary>
    /// Maps a loaded save platform to its default input scheme. Steam, GOG and unknown
    /// platforms use keyboard and mouse; the console platforms use their own pad labels.
    /// </summary>
    internal static ControlScheme SchemeForPlatform(SaveFileManager.Platform platform) => platform switch
    {
        SaveFileManager.Platform.XboxGamePass => ControlScheme.Xbox,
        SaveFileManager.Platform.PS4 => ControlScheme.PlayStation,
        SaveFileManager.Platform.Switch => ControlScheme.Switch,
        _ => ControlScheme.KeyboardMouse
    };

    /// <summary>
    /// Replaces every known <c>FE_*</c> token in <paramref name="text"/> with the label for
    /// the active scheme. Unknown tokens are returned unchanged so new game tokens degrade
    /// gracefully.
    /// </summary>
    internal static string Resolve(string? text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("FE_", StringComparison.Ordinal))
            return text ?? "";

        var labels = LabelsFor(_scheme);
        return TokenRegex.Replace(text, m => labels.TryGetValue(m.Value, out string? label) ? label : m.Value);
    }

    /// <summary>
    /// Builds the token label table for the given scheme. The table is rebuilt per call so
    /// localised mouse and touch labels follow UI language changes.
    /// </summary>
    private static Dictionary<string, string> LabelsFor(ControlScheme scheme)
    {
        if (scheme == ControlScheme.KeyboardMouse)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["FE_ALT1"] = "[E]",
                ["FE_SELECT"] = LocalisedLabel("control.mouse_left", "LMB"),
                ["FE_BACK"] = LocalisedLabel("control.mouse_right", "RMB"),
                ["FE_DESTROY"] = LocalisedLabel("control.mouse_middle", "MMB"),
                ["FE_TRANSFER"] = "[X]",
                ["FE_EXITMENU"] = LocalisedLabel("control.mouse_right", "RMB"),
                ["FE_OPTIONS"] = "[Esc]",
                ["FE_QUIT"] = "[Esc]",
                ["FE_MSGSKIP"] = "[Tab]",
                ["FE_UPLOAD_DISCOVERY"] = "[F]",
                ["FE_ASSIGN_CUSTOM_WONDER"] = "[Z]",
                ["FE_TOUCHSCREENPRESS"] = LocalisedLabel("control.touch", "Touch")
            };
        }

        if (scheme == ControlScheme.Xbox)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["FE_ALT1"] = "[X]",
                ["FE_SELECT"] = "[A]",
                ["FE_BACK"] = "[B]",
                ["FE_DESTROY"] = "[R3]",
                ["FE_TRANSFER"] = "[Y]",
                ["FE_EXITMENU"] = "[B]",
                ["FE_OPTIONS"] = "[Menu]",
                ["FE_QUIT"] = "[Menu]",
                ["FE_MSGSKIP"] = "[View]",
                ["FE_UPLOAD_DISCOVERY"] = "[X]",
                ["FE_ASSIGN_CUSTOM_WONDER"] = "[L3]"
            };
        }

        if (scheme == ControlScheme.PlayStation)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["FE_ALT1"] = "[Square]",
                ["FE_SELECT"] = "[Cross]",
                ["FE_BACK"] = "[Circle]",
                ["FE_DESTROY"] = "[R3]",
                ["FE_TRANSFER"] = "[Triangle]",
                ["FE_EXITMENU"] = "[Circle]",
                ["FE_OPTIONS"] = "[Options]",
                ["FE_QUIT"] = "[Options]",
                ["FE_MSGSKIP"] = "[Touchpad]",
                ["FE_UPLOAD_DISCOVERY"] = "[Square]",
                ["FE_ASSIGN_CUSTOM_WONDER"] = "[L3]"
            };
        }

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FE_ALT1"] = "[Y]",
            ["FE_SELECT"] = "[A]",
            ["FE_BACK"] = "[B]",
            ["FE_DESTROY"] = "[R3]",
            ["FE_TRANSFER"] = "[X]",
            ["FE_EXITMENU"] = "[B]",
            ["FE_OPTIONS"] = "[Plus]",
            ["FE_QUIT"] = "[Plus]",
            ["FE_MSGSKIP"] = "[Minus]",
            ["FE_UPLOAD_DISCOVERY"] = "[Y]",
            ["FE_ASSIGN_CUSTOM_WONDER"] = "[L3]",
            ["FE_TOUCHSCREENPRESS"] = LocalisedLabel("control.touch", "Touch")
        };
    }

    /// <summary>
    /// Returns the bracketed localised label for a UI string key, falling back to the
    /// English abbreviation when the key is missing (for example before strings are loaded).
    /// </summary>
    private static string LocalisedLabel(string key, string fallback)
    {
        string value = UiStrings.Get(key);
        if (string.IsNullOrEmpty(value) || string.Equals(value, key, StringComparison.Ordinal))
            value = fallback;
        return "[" + value + "]";
    }
}
