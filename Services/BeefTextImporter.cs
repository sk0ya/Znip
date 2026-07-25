using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Microsoft.Win32;
using Znip.Models;

namespace Znip.Services;

/// <summary>
/// BeefText (https://beeftext.org) からのスニペット・設定の移行。
/// comboList.json のフォーマットと設定のレジストリキーは BeefText のソースコード
/// (github.com/xmichelo/Beeftext) の実装を基に再現している。バージョン差異等で
/// 想定と異なる場合は例外/null 扱いにして、既定値のまま動作を続ける。
/// </summary>
public static class BeefTextImporter
{
    private const string RegistryKeyPath = @"Software\beeftext.org\Beeftext";

    public class ImportedCombo
    {
        public string Keyword { get; set; } = "";
        public string Name { get; set; } = "";
        public string Snippet { get; set; } = "";
        public bool Enabled { get; set; } = true;
        public string GroupUuid { get; set; } = "";
    }

    public class ImportedGroup
    {
        public string Uuid { get; set; } = "";
        public string Name { get; set; } = "";
    }

    public class ImportResult
    {
        public List<ImportedCombo> Combos { get; } = new();
        public List<ImportedGroup> Groups { get; } = new();
        public bool? AutoExpandEnabled { get; set; }
        public bool? LaunchAtStartup { get; set; }
        public ModifierKeys? HotkeyModifiers { get; set; }
        public Key? HotkeyKey { get; set; }
    }

    /// <summary>BeefText のコンボ一覧ファイルの既定の場所を探す。見つからなければ null。</summary>
    public static string? FindComboListPath()
    {
        // ユーザーが保存先フォルダを変更している場合はレジストリに上書きパスが入っている
        var overrideFolder = ReadRegistryString("ComboListFolderPath");
        if (!string.IsNullOrEmpty(overrideFolder))
        {
            var candidate = Path.Combine(overrideFolder, "comboList.json");
            if (File.Exists(candidate)) return candidate;
        }

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] candidates =
        {
            Path.Combine(local, "beeftext.org", "Beeftext", "comboList.json"),
            Path.Combine(roaming, "beeftext.org", "Beeftext", "comboList.json"),
        };
        return Array.Find(candidates, File.Exists);
    }

    /// <summary>指定した comboList.json とレジストリ設定から取り込み内容を読み取る。</summary>
    public static ImportResult Import(string comboListPath)
    {
        var result = new ImportResult();

        using (var doc = JsonDocument.Parse(File.ReadAllText(comboListPath)))
        {
            if (doc.RootElement.TryGetProperty("combos", out var combosEl) && combosEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in combosEl.EnumerateArray())
                {
                    var combo = new ImportedCombo
                    {
                        Keyword = GetString(c, "keyword"),
                        Name = GetString(c, "name"),
                        Snippet = ConvertVariableSyntax(GetString(c, "snippet")),
                        Enabled = GetBool(c, "enabled", true),
                        GroupUuid = GetString(c, "group"),
                    };
                    if (!string.IsNullOrWhiteSpace(combo.Keyword) && combo.Enabled)
                        result.Combos.Add(combo);
                }
            }

            if (doc.RootElement.TryGetProperty("groups", out var groupsEl) && groupsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var g in groupsEl.EnumerateArray())
                {
                    var uuid = GetString(g, "uuid");
                    if (string.IsNullOrWhiteSpace(uuid)) continue;
                    result.Groups.Add(new ImportedGroup { Uuid = uuid, Name = GetString(g, "name") });
                }
            }
        }

        TryReadSettings(result);
        return result;
    }

    /// <summary>
    /// BeefText の #{...} 変数構文のうち、Znip の {...} 構文へ安全に置き換えられる
    /// ものだけを変換する(clipboard/cursor/date/time/dateTime[:書式])。
    /// combo:/lower:/upper:/input:/envVar:/powershell:/key:/shortcut:/delay: や
    /// dateTime のシフト指定(#{dateTime:+1d:書式})など Znip に対応がないものは
    /// そのまま残す(展開時はリテラル文字列として表示される)。
    /// </summary>
    private static string ConvertVariableSyntax(string content)
    {
        var text = content;
        text = Regex.Replace(text, @"#\{cursor\}", "{cursor}");
        text = Regex.Replace(text, @"#\{clipboard\}", "{clipboard}");
        text = Regex.Replace(text, @"#\{date\}", "{date}");
        text = Regex.Replace(text, @"#\{time\}", "{time}");
        // dateTime[:書式] のみ変換。dateTime:シフト:書式 は書式部分に ':' を含むため対象外。
        text = Regex.Replace(text, @"#\{dateTime(?::([^{}:]+))?\}",
            m => m.Groups[1].Success ? $"{{date:{m.Groups[1].Value}}}" : "{date}");
        return text;
    }

    private static string GetString(JsonElement el, string prop) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static bool GetBool(JsonElement el, string prop, bool fallback) =>
        el.TryGetProperty(prop, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : fallback;

    private static void TryReadSettings(ImportResult result)
    {
        try
        {
            result.AutoExpandEnabled = ReadRegistryBool("UseAutomaticSubstitution");
            result.LaunchAtStartup = ReadRegistryBool("AutoStartAtLogin");

            // バージョンにより「結合int32(ComboPickerShortcut)」形式と、
            // 旧形式の「Modifiers/KeyCode別々のDWORD」形式のどちらかで保存されている。
            ModifierKeys mods; Key key; bool decoded = false;
            var legacyMods = ReadRegistryInt("ComboPickerShortcutModifiers");
            var legacyKeyCode = ReadRegistryInt("ComboPickerShortcutKeyCode");
            if (legacyMods is int lm && legacyKeyCode is int lk && (lm != 0 || lk != 0))
                decoded = TryDecodeShortcut(lm, lk, out mods, out key);
            else
            {
                mods = ModifierKeys.None; key = Key.None;
            }
            if (!decoded)
            {
                var combined = ReadRegistryInt("ComboPickerShortcut");
                if (combined.HasValue)
                {
                    const int modMaskAll = 0x02000000 | 0x04000000 | 0x08000000 | 0x10000000 | 0x20000000 | 0x40000000;
                    decoded = TryDecodeShortcut(combined.Value & modMaskAll, combined.Value & ~modMaskAll, out mods, out key);
                }
            }
            if (decoded)
            {
                result.HotkeyModifiers = mods;
                result.HotkeyKey = key;
            }
        }
        catch { /* レジストリが無い/想定外の形式の場合は設定の移行だけスキップする */ }
    }

    /// <summary>
    /// Qt::KeyboardModifiers のビットフラグと Qt::Key の値から Znip のホットキー表現へ変換する。
    /// 英数字+Spaceキーの組み合わせのみ復元を試みる(それ以外は Znip 側の既定ホットキーのまま)。
    /// </summary>
    private static bool TryDecodeShortcut(int qtModifiers, int qtKey, out ModifierKeys modifiers, out Key key)
    {
        modifiers = ModifierKeys.None;
        key = Key.None;

        const int shift = 0x02000000, ctrl = 0x04000000, alt = 0x08000000, meta = 0x10000000;

        if ((qtModifiers & shift) != 0) modifiers |= ModifierKeys.Shift;
        if ((qtModifiers & ctrl) != 0) modifiers |= ModifierKeys.Control;
        if ((qtModifiers & alt) != 0) modifiers |= ModifierKeys.Alt;
        if ((qtModifiers & meta) != 0) modifiers |= ModifierKeys.Windows;
        if (modifiers == ModifierKeys.None) return false;

        if (qtKey is >= 'A' and <= 'Z')
        {
            key = Enum.Parse<Key>(((char)qtKey).ToString());
            return true;
        }
        if (qtKey is >= '0' and <= '9')
        {
            key = Enum.Parse<Key>("D" + (char)qtKey);
            return true;
        }
        if (qtKey == 0x20) { key = Key.Space; return true; }

        return false;
    }

    private static string? ReadRegistryString(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
        return key?.GetValue(name) as string;
    }

    private static bool? ReadRegistryBool(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
        return key?.GetValue(name) switch
        {
            null => null,
            bool b => b,
            string s when bool.TryParse(s, out var b) => b,
            int i => i != 0,
            _ => null,
        };
    }

    private static int? ReadRegistryInt(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
        return key?.GetValue(name) switch
        {
            null => null,
            int i => i,
            string s when int.TryParse(s, out var i) => i,
            _ => null,
        };
    }
}
