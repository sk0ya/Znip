using System.Windows;
using Microsoft.Win32;
using Znip.Models;

namespace Znip.Services;

/// <summary>
/// アプリ全体の配色を切り替える。App.xaml のマージ済みディクショナリの
/// 先頭(パレット)だけを差し替えるので、開いているウィンドウにもその場で反映される
/// (そのためコントロール側は色を DynamicResource で参照している)。
/// </summary>
public static class ThemeManager
{
    /// <summary>App.xaml でパレットを置いている位置</summary>
    private const int PaletteIndex = 0;

    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static AppTheme _current = AppTheme.System;
    private static bool _watchingSystem;

    /// <summary>実際に適用されている配色(System の場合は解決後の値)</summary>
    public static bool IsDark { get; private set; }

    public static void Apply(AppTheme theme)
    {
        _current = theme;
        bool dark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsSystemDark(),
        };

        var source = new Uri(dark ? "Themes/Dark.xaml" : "Themes/Light.xaml", UriKind.Relative);
        var palette = new ResourceDictionary { Source = source };

        var merged = Application.Current.Resources.MergedDictionaries;
        if (merged.Count > PaletteIndex) merged[PaletteIndex] = palette;
        else merged.Insert(PaletteIndex, palette);

        IsDark = dark;

        if (theme == AppTheme.System) StartWatchingSystem();
    }

    /// <summary>Windows の「アプリのモード」がダークかどうか</summary>
    private static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            // AppsUseLightTheme: 1 = ライト, 0 = ダーク。値が無い環境ではライト扱い。
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>「システムに従う」の間だけ、OS 側の切り替えを拾って再適用する</summary>
    private static void StartWatchingSystem()
    {
        if (_watchingSystem) return;
        _watchingSystem = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            if (e.Category != UserPreferenceCategory.General) return;
            if (_current != AppTheme.System) return;
            Application.Current?.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
        };
    }
}
