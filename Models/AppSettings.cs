using System.Text.Json.Serialization;
using System.Windows.Input;

namespace Znip.Models;

public class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ModifierKeys HotkeyModifiers { get; set; } = ModifierKeys.Control | ModifierKeys.Shift;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Key HotkeyKey { get; set; } = Key.Space;

    /// <summary>キーワード入力による自動展開を有効にするか</summary>
    public bool AutoExpandEnabled { get; set; } = true;

    /// <summary>Windows起動時に自動で起動するか</summary>
    public bool LaunchAtStartup { get; set; } = false;

    /// <summary>起動時のBeefText移行確認を表示済みか(一度だけ確認する)</summary>
    public bool BeefTextImportPrompted { get; set; } = false;

    public string HotkeyDisplayText()
    {
        var parts = new List<string>();
        if (HotkeyModifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (HotkeyModifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (HotkeyModifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (HotkeyModifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(HotkeyKey.ToString());
        return string.Join(" + ", parts);
    }
}
