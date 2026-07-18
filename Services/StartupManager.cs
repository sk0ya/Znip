using System.Diagnostics;

namespace Znip.Services;

/// <summary>
/// Windows 起動時の自動起動。
/// 本アプリは管理者権限で動作するため、レジストリの Run キーではなく
/// タスクスケジューラ(最上位の特権で実行)を使う。これにより
/// ログオン時に UAC プロンプトなしで管理者として起動できる。
/// </summary>
public static class StartupManager
{
    private const string TaskName = "Znip";

    public static void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            var exe = Environment.ProcessPath
                ?? throw new InvalidOperationException("実行ファイルのパスを取得できません。");
            RunSchtasks($"/Create /F /RL HIGHEST /SC ONLOGON /TN \"{TaskName}\" /TR \"\\\"{exe}\\\"\"",
                throwOnError: true);
        }
        else
        {
            RunSchtasks($"/Delete /F /TN \"{TaskName}\"", throwOnError: false);
        }
    }

    public static bool IsEnabled() =>
        RunSchtasks($"/Query /TN \"{TaskName}\"", throwOnError: false) == 0;

    private static int RunSchtasks(string args, bool throwOnError)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "schtasks.exe",
            Arguments = args,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        using var p = Process.Start(psi)!;
        string stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(10000);
        if (throwOnError && p.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? $"schtasks が失敗しました (code {p.ExitCode})" : stderr.Trim());
        return p.ExitCode;
    }
}
