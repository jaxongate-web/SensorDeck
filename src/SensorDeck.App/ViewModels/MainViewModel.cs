using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SensorDeck.App.Services;
using SensorDeck.Core.Models;
using SensorDeck.Core.Sensors;
using SensorDeck.Core.Settings;

namespace SensorDeck.App.ViewModels;

public enum AccessAction { None, RestartAsAdmin, GetDriver }

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public const string PawnIoUrl = "https://pawnio.eu/";

    private readonly IHardwareMonitor _monitor;
    private readonly Dispatcher _dispatcher;
    private readonly SettingsService _settings;
    private int _pendingUpdate;
    private bool _bannerDismissed;
    private AccessAction _dismissedAction;

    public DashboardViewModel Dashboard { get; } = new();
    public GraphsViewModel Graphs { get; } = new();
    public HardwareViewModel Hardware { get; } = new();
    public SettingsViewModel Settings { get; }

    [ObservableProperty] public partial object CurrentPage { get; set; }
    [ObservableProperty] public partial string StatusText { get; set; } = "Detecting hardware…";
    [ObservableProperty] public partial bool IsLive { get; set; }
    [ObservableProperty] public partial bool AlwaysOnTop { get; set; }

    [ObservableProperty] public partial string? BannerText { get; set; }
    [ObservableProperty] public partial string? BannerButtonText { get; set; }
    [ObservableProperty] public partial AccessAction BannerAction { get; set; }

    /// <summary>Short live summary for the tray icon tooltip.</summary>
    public string TrayText { get; private set; } = "SensorDeck";

    /// <summary>Called before relaunching elevated so the new instance isn't blocked by single-instance checks.</summary>
    public Action? BeforeRestart { get; set; }

    public string Version { get; } =
        "v" + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    public MainViewModel(IHardwareMonitor monitor, Dispatcher dispatcher, SettingsService settings)
    {
        _monitor = monitor;
        _dispatcher = dispatcher;
        _settings = settings;
        Settings = new SettingsViewModel(settings, () => _monitor.Latest?.Status);
        CurrentPage = Dashboard;

        ApplySettings(settings.Current, initial: true);
        settings.Changed += s => ApplySettings(s, initial: false);

        _monitor.Initialized += (_, _) => _dispatcher.BeginInvoke(OnInitialized);
        _monitor.SnapshotReady += OnSnapshot;
        _monitor.Start();
    }

    private void ApplySettings(AppSettings s, bool initial)
    {
        _monitor.Interval = TimeSpan.FromSeconds(s.RefreshSeconds);
        Dashboard.Settings = s;
        AlwaysOnTop = s.AlwaysOnTop;
        if (initial) Graphs.SelectRange(s.DefaultGraphMinutes);
        else Graphs.RefreshDisplay();
        if (!initial && _monitor.Latest is { } latest) Apply(latest); // reflect unit/threshold changes immediately
    }

    private void OnInitialized()
    {
        if (_monitor.SystemInfo is { } info)
        {
            Hardware.Load(info);
            Dashboard.SystemInfo = info;
        }

        if (_monitor.InitializationError is { } err)
        {
            StatusText = "Sensor access failed";
            BannerText = $"Hardware monitoring could not start: {err}";
            BannerAction = AccessAction.None;
        }
    }

    private void OnSnapshot(object? sender, HardwareSnapshot snap)
    {
        // Coalesce: if the UI hasn't consumed the previous snapshot yet, don't queue another.
        if (Interlocked.Exchange(ref _pendingUpdate, 1) == 1) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            Interlocked.Exchange(ref _pendingUpdate, 0);
            Apply(_monitor.Latest ?? snap, record: true);
        });
    }

    private void Apply(HardwareSnapshot snap) => Apply(snap, record: false);

    private void Apply(HardwareSnapshot snap, bool record)
    {
        Dashboard.Apply(snap);
        if (record) Graphs.Apply(snap, _monitor.Interval); // history keeps recording even when the page isn't shown
        Hardware.ApplyStatus(snap.Status);
        if (CurrentPage == Hardware) Hardware.ApplyRaw(snap.RawSensors);
        IsLive = true;
        double sec = _monitor.Interval.TotalSeconds;
        StatusText = $"Live · every {sec:0.#} s";
        UpdateBanner(snap.Status);
        TrayText = BuildTrayText(snap);
    }

    private static string BuildTrayText(HardwareSnapshot s)
    {
        static string Part(string label, SensorValue temp, SensorValue load)
        {
            if (temp.Value is double t) { var (v, u) = Format.Temperature(t); return $"{label} {v}{u}"; }
            if (load.Value is double l) return $"{label} {l:0}%";
            return $"{label} n/a";
        }
        var parts = new List<string>();
        if (s.PrimaryCpu is { } c) parts.Add(Part("CPU", c.Temperature, c.Usage));
        if (s.PrimaryGpu is { } g) parts.Add(Part("GPU", g.Temperature, g.Usage));
        if (s.Memory.LoadPercent.Value is double m) parts.Add($"RAM {m:0}%");
        return "SensorDeck\n" + string.Join(" · ", parts);
    }

    private void UpdateBanner(MonitorStatus status)
    {
        AccessAction action = !status.IsElevated ? AccessAction.RestartAsAdmin
            : !status.IsDriverInstalled ? AccessAction.GetDriver
            : AccessAction.None;

        // Stay dismissed until the situation changes (e.g. after installing the driver).
        if (_bannerDismissed && action == _dismissedAction) return;
        _bannerDismissed = false;

        BannerAction = action;
        (BannerText, BannerButtonText) = action switch
        {
            AccessAction.RestartAsAdmin => ("Running without administrator rights. CPU temperature, clock speed, storage temperatures and motherboard fans can't be read.", "Restart as administrator"),
            AccessAction.GetDriver => ("CPU temperature, clock speed and motherboard fans need the free, signed PawnIO driver used by LibreHardwareMonitor.", "Get PawnIO"),
            _ => ((string?)null, (string?)null),
        };
    }

    [RelayCommand]
    private void Navigate(string page)
    {
        CurrentPage = page switch
        {
            "Graphs" => Graphs,
            "Hardware" => Hardware,
            "Settings" => Settings,
            _ => Dashboard,
        };
        // Collecting every raw sensor costs a little each tick, so only do it while the table is visible.
        _monitor.CaptureRawSensors = CurrentPage == Hardware;
        if (CurrentPage == Hardware && _monitor.Latest is { RawSensors.Count: > 0 } latest) Hardware.ApplyRaw(latest.RawSensors);
        if (CurrentPage == Settings) Settings.Refresh();
    }

    [RelayCommand]
    private void BannerClicked()
    {
        switch (BannerAction)
        {
            case AccessAction.GetDriver:
                Process.Start(new ProcessStartInfo(PawnIoUrl) { UseShellExecute = true });
                break;
            case AccessAction.RestartAsAdmin:
                RestartAsAdministrator();
                break;
        }
    }

    public void RestartAsAdministrator()
    {
        try
        {
            _settings.Flush();
            BeforeRestart?.Invoke();
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            ((App)Application.Current).ExitApp();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User declined the UAC prompt — keep running unelevated.
        }
    }

    [RelayCommand]
    private void DismissBanner()
    {
        _bannerDismissed = true;
        _dismissedAction = BannerAction;
        BannerText = null;
    }

    public void Dispose() => _monitor.Dispose();
}
