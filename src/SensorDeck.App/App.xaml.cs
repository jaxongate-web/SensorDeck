using System.Windows;
using System.Windows.Threading;
using SensorDeck.App.Services;
using SensorDeck.App.ViewModels;
using SensorDeck.Core.Sensors;
using SensorDeck.Core.Settings;

namespace SensorDeck.App;

public partial class App : Application
{
    private const string InstanceMutexName = @"Local\SensorDeck.SingleInstance";
    private const string ShowEventName = @"Local\SensorDeck.ShowWindow";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _showEvent;
    private MainViewModel? _main;
    private SettingsService? _settings;
    private TrayService? _tray;
    private MainWindow? _window;
    private bool _exiting;

    public bool IsExiting => _exiting;
    public SettingsService Settings => _settings!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        if (!AcquireSingleInstance())
        {
            Shutdown();
            return;
        }

        bool launchedMinimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

        _settings = new SettingsService(new SettingsStore(SettingsStore.DefaultPath));
        _main = new MainViewModel(new HardwareMonitor(), Dispatcher, _settings) { BeforeRestart = ReleaseSingleInstance };

        _tray = new TrayService();
        _tray.OpenRequested += ShowMainWindow;
        _tray.ExitRequested += ExitApp;

        _window = new MainWindow { DataContext = _main };
        MainWindow = _window;
        _window.Closed += (_, _) => ExitApp();

        ApplyTraySettings(_settings.Current);
        _settings.Changed += ApplyTraySettings;

        // Keep the tray tooltip live (cheap: only touches a string once per second).
        var trayTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background,
            (_, _) => { if (_tray.IsVisible) _tray.SetTooltip(_main.TrayText); }, Dispatcher);
        trayTimer.Start();

        bool startHidden = launchedMinimized || _settings.Current.StartMinimized;
        if (startHidden && _settings.Current.UsesTray)
        {
            // Window stays hidden; the tray icon is the way back in.
        }
        else
        {
            if (startHidden) _window.WindowState = WindowState.Minimized;
            _window.Show();
        }
    }

    private void ApplyTraySettings(AppSettings s)
    {
        if (_tray is null) return;
        // Also show the icon whenever the window is hidden, so the app can never become unreachable.
        _tray.IsVisible = s.UsesTray || _window is { IsVisible: false };
    }

    public void ShowMainWindow()
    {
        if (_window is null) return;
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        _window.Focus();
    }

    /// <summary>Hides the window to the tray (used by minimise/close-to-tray).</summary>
    public void HideToTray()
    {
        if (_window is null || _tray is null) return;
        _tray.IsVisible = true;
        _window.Hide();
    }

    public void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        _settings?.Flush();
        _tray?.Dispose();
        _window?.Close();
        _main?.Dispose();
        ReleaseSingleInstance();
        Shutdown();
    }

    // ----- Single instance: a second launch just brings the running window forward -----

    private bool AcquireSingleInstance()
    {
        try
        {
            _instanceMutex = new Mutex(true, InstanceMutexName, out bool createdNew);
            if (!createdNew)
            {
                SignalExistingInstance();
                _instanceMutex.Dispose();
                _instanceMutex = null;
                return false;
            }
        }
        catch (UnauthorizedAccessException)
        {
            // Held by an instance running at a different privilege level: it's already running.
            SignalExistingInstance();
            return false;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        var listener = new Thread(() =>
        {
            try
            {
                while (_showEvent.WaitOne())
                {
                    if (_exiting) break;
                    Dispatcher.BeginInvoke(ShowMainWindow);
                }
            }
            catch (ObjectDisposedException) { }
        }) { IsBackground = true, Name = "SensorDeck single-instance listener" };
        listener.Start();
        return true;
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(ShowEventName);
            ev.Set();
        }
        catch
        {
            MessageBox.Show("SensorDeck is already running. Look for its icon in the notification area.",
                "SensorDeck", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ReleaseSingleInstance()
    {
        try { _instanceMutex?.ReleaseMutex(); } catch { }
        _instanceMutex?.Dispose();
        _instanceMutex = null;
        var ev = _showEvent;
        _showEvent = null;
        try { ev?.Set(); } catch { }   // wake the listener so it can exit
        ev?.Dispose();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_exiting) { _settings?.Flush(); _tray?.Dispose(); _main?.Dispose(); ReleaseSingleInstance(); }
        base.OnExit(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"An unexpected error occurred:\n\n{e.Exception.Message}", "SensorDeck",
            MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
