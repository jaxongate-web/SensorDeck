using LibreHardwareMonitor.Hardware;
using SensorDeck.Core.Info;
using SensorDeck.Core.Models;

namespace SensorDeck.Core.Sensors;

public interface IHardwareMonitor : IDisposable
{
    /// <summary>Raised on the monitor's background thread after every polling tick.</summary>
    event EventHandler<HardwareSnapshot>? SnapshotReady;

    /// <summary>Raised once hardware detection has finished (or failed).</summary>
    event EventHandler? Initialized;

    SystemInfo? SystemInfo { get; }
    HardwareSnapshot? Latest { get; }
    string? InitializationError { get; }
    TimeSpan Interval { get; set; }
    bool CaptureRawSensors { get; set; }
    void Start();
}

/// <summary>
/// Owns the LibreHardwareMonitor <see cref="Computer"/>. All LHM access happens on one dedicated
/// background thread (LHM is not thread-safe); consumers only ever see immutable snapshots.
/// </summary>
public sealed class HardwareMonitor : IHardwareMonitor
{
    private readonly AutoResetEvent _wake = new(false);
    private readonly SnapshotBuilder _builder = new();
    private Thread? _thread;
    private volatile bool _stopping;
    private TimeSpan _interval = TimeSpan.FromSeconds(1);

    public event EventHandler<HardwareSnapshot>? SnapshotReady;
    public event EventHandler? Initialized;

    public SystemInfo? SystemInfo { get; private set; }
    public HardwareSnapshot? Latest { get; private set; }
    public string? InitializationError { get; private set; }
    public bool CaptureRawSensors { get; set; }

    public TimeSpan Interval
    {
        get => _interval;
        set
        {
            _interval = value < TimeSpan.FromMilliseconds(250) ? TimeSpan.FromMilliseconds(250) : value;
            _wake.Set();
        }
    }

    public void Start()
    {
        if (_thread is not null) return;
        _thread = new Thread(Run) { IsBackground = true, Name = "SensorDeck hardware monitor", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    private void Run()
    {
        Computer? computer = null;
        MonitorStatus status;
        try
        {
            status = SystemAccess.Detect();
            computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsMotherboardEnabled = true,
                IsControllerEnabled = true,
                IsStorageEnabled = true,
            };
            computer.Open();
            UpdateAll(computer);
            SystemInfo = SystemInfoReader.Read(computer, status);
        }
        catch (Exception ex)
        {
            InitializationError = ex.Message;
            Initialized?.Invoke(this, EventArgs.Empty);
            try { computer?.Close(); } catch { /* best effort */ }
            return;
        }

        Initialized?.Invoke(this, EventArgs.Empty);

        while (!_stopping)
        {
            var started = DateTime.UtcNow;
            try
            {
                UpdateAll(computer);
                var snap = _builder.Build(computer.Hardware, status, SystemInfo.MemorySummary, CaptureRawSensors);
                Latest = snap;
                SnapshotReady?.Invoke(this, snap);
            }
            catch
            {
                // A single bad read (e.g. a drive disappearing) must never stop monitoring.
            }

            var wait = _interval - (DateTime.UtcNow - started);
            if (wait > TimeSpan.Zero) _wake.WaitOne(wait);
        }

        try { computer.Close(); } catch { /* best effort */ }
    }

    private static void UpdateAll(Computer computer)
    {
        foreach (var hw in computer.Hardware) Update(hw);

        static void Update(IHardware hw)
        {
            hw.Update();
            foreach (var sub in hw.SubHardware) Update(sub);
        }
    }

    public void Dispose()
    {
        _stopping = true;
        _wake.Set();
        _thread?.Join(TimeSpan.FromSeconds(3));
        _wake.Dispose();
    }
}
