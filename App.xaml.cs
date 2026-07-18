using System.Windows;
using Znip.Services;
using Znip.Views;
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Znip;

public partial class App : System.Windows.Application
{
    private const string MutexName = "Znip_SingleInstance_Mutex";
    private const string ShowEventName = "Znip_ShowSettings_Event";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private WinForms.NotifyIcon? _notifyIcon;

    public SnippetStore Store { get; } = new();
    public HotkeyManager Hotkeys { get; private set; } = null!;
    public KeyboardHook Hook { get; private set; } = null!;

    private MainWindow? _mainWindow;
    private PickerWindow? _pickerWindow;

    public static new App Current => (App)System.Windows.Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 二重起動防止。既存インスタンスがあれば設定画面を出すよう合図して終了
        _mutex = new Mutex(true, MutexName, out bool isNew);
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        if (!isNew)
        {
            _showEvent.Set();
            Shutdown();
            return;
        }

        base.OnStartup(e);

        Store.Load();

        Hotkeys = new HotkeyManager();
        Hotkeys.HotkeyPressed += ShowPicker;
        RegisterHotkeyFromSettings();

        Hook = new KeyboardHook(Store, Dispatcher);
        if (Store.Settings.AutoExpandEnabled)
            Hook.Start();

        CreateTrayIcon();
        WatchShowEvent();

        // トレイ常駐アプリだが、手動起動時は設定画面を表示するのが親切
        ShowSettings();
    }

    public void RegisterHotkeyFromSettings()
    {
        var s = Store.Settings;
        if (!Hotkeys.Register(s.HotkeyModifiers, s.HotkeyKey))
        {
            MessageBox.Show(
                $"ホットキー {s.HotkeyDisplayText()} を登録できませんでした。\n他のアプリと競合している可能性があります。設定画面から変更してください。",
                "Znip", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void ShowSettings()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow();
            _mainWindow.Closed += (_, _) => _mainWindow = null;
        }
        _mainWindow.Show();
        if (_mainWindow.WindowState == WindowState.Minimized)
            _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void ShowPicker()
    {
        if (_pickerWindow != null)
        {
            _pickerWindow.Activate();
            return;
        }
        // ピッカーにフォーカスが移る前に、貼り付け先のウィンドウを覚えておく
        var target = NativeMethods.GetForegroundWindow();
        _pickerWindow = new PickerWindow(Store, target);
        _pickerWindow.Closed += (_, _) => _pickerWindow = null;
        _pickerWindow.Show();
        _pickerWindow.Activate();
    }

    private void CreateTrayIcon()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("スニペットを開く (ピッカー)", null, (_, _) => Dispatcher.Invoke(ShowPicker));
        menu.Items.Add("設定...", null, (_, _) => Dispatcher.Invoke(ShowSettings));
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("終了", null, (_, _) => Dispatcher.Invoke(ExitApp));

        _notifyIcon = new WinForms.NotifyIcon
        {
            Icon = CreateAppIcon(),
            Text = $"Znip — {Store.Settings.HotkeyDisplayText()} でピッカーを開く",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowSettings);
    }

    /// <summary>アイコンファイル不要の簡易アイコン("Z" を描画)</summary>
    private static Drawing.Icon CreateAppIcon()
    {
        using var bmp = new Drawing.Bitmap(32, 32);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            g.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = Drawing.Text.TextRenderingHint.AntiAlias;
            using var bg = new Drawing.SolidBrush(Drawing.Color.FromArgb(79, 109, 245));
            g.FillEllipse(bg, 0, 0, 31, 31);
            using var font = new Drawing.Font("Segoe UI", 16, Drawing.FontStyle.Bold, Drawing.GraphicsUnit.Pixel);
            var size = g.MeasureString("Z", font);
            g.DrawString("Z", font, Drawing.Brushes.White, (32 - size.Width) / 2f, (32 - size.Height) / 2f);
        }
        return Drawing.Icon.FromHandle(bmp.GetHicon());
    }

    private void WatchShowEvent()
    {
        var thread = new Thread(() =>
        {
            while (_showEvent!.WaitOne())
                Dispatcher.BeginInvoke(ShowSettings);
        })
        { IsBackground = true, Name = "ZnipShowEventWatcher" };
        thread.Start();
    }

    public void ExitApp()
    {
        Store.SaveNow();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        Hook?.Dispose();
        Hotkeys?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
