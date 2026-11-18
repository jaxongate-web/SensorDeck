using LibreHardwareMonitor.Hardware;

namespace SensorDeck.Core.Tests;

internal sealed class FakeHardware : IHardware
{
    private readonly List<ISensor> _sensors = new();
    private readonly List<IHardware> _sub = new();

    public FakeHardware(HardwareType type, string name)
    {
        HardwareType = type;
        Name = name;
        Identifier = new Identifier(type.ToString().ToLowerInvariant(), name.Replace(' ', '-'));
    }

    public FakeHardware With(SensorType type, string name, float? value)
    {
        _sensors.Add(new FakeSensor(this, type, name, value, _sensors.Count));
        return this;
    }

    public FakeHardware WithSub(IHardware sub)
    {
        _sub.Add(sub);
        return this;
    }

    public void Set(string name, SensorType type, float? value) =>
        ((FakeSensor)_sensors.First(s => s.Name == name && s.SensorType == type)).Value = value;

    public HardwareType HardwareType { get; }
    public Identifier Identifier { get; }
    public string Name { get; set; }
    public IHardware? Parent => null;
    public ISensor[] Sensors => _sensors.ToArray();
    public IHardware[] SubHardware => _sub.ToArray();
    public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
    public string GetReport() => "";
    public void Update() { }
    public void Accept(IVisitor visitor) { }
    public void Traverse(IVisitor visitor) { }
#pragma warning disable CS0067
    public event SensorEventHandler? SensorAdded;
    public event SensorEventHandler? SensorRemoved;
#pragma warning restore CS0067
}

internal sealed class FakeSensor(IHardware hardware, SensorType type, string name, float? value, int index) : ISensor
{
    public IControl? Control => null;
    public IHardware Hardware { get; } = hardware;
    public Identifier Identifier { get; } = new(hardware.Identifier, type.ToString().ToLowerInvariant(), index.ToString());
    public int Index { get; } = index;
    public bool IsDefaultHidden => false;
    public float? Max => Value;
    public float? Min => Value;
    public string Name { get; set; } = name;
    public IReadOnlyList<IParameter> Parameters => [];
    public SensorType SensorType { get; } = type;
    public float? Value { get; set; } = value;
    public IEnumerable<SensorValue> Values => [];
    public TimeSpan ValuesTimeWindow { get; set; }
    public void ResetMin() { }
    public void ResetMax() { }
    public void ClearValues() { }
    public void Accept(IVisitor visitor) { }
    public void Traverse(IVisitor visitor) { }
}
