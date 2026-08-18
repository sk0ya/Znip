using System.Text.Json.Serialization;
using System.Windows.Input;

namespace Znip.Models;

/// <summary>画面の配色。System は Windows の「アプリのモード」に従う。</summary>
public enum AppTheme { System, Light, Dark }

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

    /// <summary>画面の配色(ライト / ダーク / システムに従う)</summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppTheme Theme { get; set; } = AppTheme.System;

    public string HotkeyDisplayText()
    {
        var parts = new List<string>();
        if (HotkeyModifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (HotkeyModifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (HotkeyModifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (HotkeyModifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(KeyDisplayText(HotkeyKey));
        return string.Join(" + ", parts);
    }

    /// <summary>
    /// Key の列挙名をそのまま出すと "OemComma" のように読みにくいので、実際の刻印に置き換える。
    /// JIS / US で刻印が変わらないキーだけを対象にし、それ以外は列挙名のままにする。
    /// </summary>
    private static string KeyDisplayText(Key key) => key switch
    {
        Key.OemComma => ",",
        Key.OemPeriod => ".",
        Key.OemMinus => "-",
        Key.OemQuestion => "/",
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => $"テンキー{key - Key.NumPad0}",
        Key.Prior => "PageUp",
        Key.Next => "PageDown",
        Key.Return => "Enter",
        Key.Capital => "CapsLock",
        _ => key.ToString(),
    };
}
