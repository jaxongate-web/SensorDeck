# SensorDeck — Project Plan

A free, lightweight Windows hardware monitor. C# / .NET 10 / WPF / LibreHardwareMonitorLib.

## Principles

- **Real data only.** Every number comes from a hardware sensor via LibreHardwareMonitor
  (or SMBIOS for static RAM details). Nothing is estimated or faked.
- **Unsupported is explicit.** A reading that the hardware/driver does not expose is shown
  as `Not supported` (greyed, with a tooltip saying why) — never as `0`.
- **Lightweight.** One background polling thread, no heavy chart library (custom
  `StreamGeometry` line graphs), fixed-size ring buffers for history, no telemetry,
  no network access.
- **Free.** No accounts, no ads, no paid tiers. MPL-2.0 compatible (LibreHardwareMonitor's licence).

## Solution layout

```
SensorDeck.sln
├─ src/
│  ├─ SensorDeck.Core/            Class library — no WPF dependency
│  │  ├─ Models/                  SensorValue, ComponentSnapshot, HardwareSnapshot, FanReading…
│  │  ├─ Sensors/
│  │  │  ├─ IHardwareMonitor.cs   Abstraction the UI talks to
│  │  │  ├─ LhmHardwareMonitor.cs LibreHardwareMonitor implementation (polling, snapshot)
│  │  │  └─ SensorSelector.cs     Picks the right sensor per vendor (Intel/AMD/NVIDIA…)
│  │  ├─ Info/                    Static hardware identification (names, RAM type, capacity)
│  │  └─ History/RingBuffer.cs    Fixed-size sample history for graphs
│  │
│  ├─ SensorDeck.App/             WPF app (MVVM, CommunityToolkit.Mvvm)
│  │  ├─ Themes/                  Colors.xaml, Controls.xaml (dark + blue accent)
│  │  ├─ Controls/                MetricCard, LiveGraph, StatusPill
│  │  ├─ ViewModels/              Shell, Dashboard, Graphs, HardwareInfo, Settings
│  │  ├─ Views/                   One UserControl per page
│  │  ├─ Services/                SettingsService (JSON in %AppData%), TrayService
│  │  └─ app.manifest             requireAdministrator (needed for CPU temps / SuperIO fans)
│  │
│  └─ SensorDeck.Probe/           Console tool: dumps every sensor LHM sees on this PC.
│                                 Used to test the sensor layer on real hardware.
└─ tests/
   └─ SensorDeck.Core.Tests/      xUnit: sensor selection against fake IHardware/ISensor trees,
                                  ring buffer, unit conversion, "unsupported" handling
```

## Interface design

Borderless window, custom title bar, left navigation rail, content area.

```
┌──────────────────────────────────────────────────────────────────────────┐
│ ◆ SensorDeck                                              ─   ▢   ✕     │
├────────┬─────────────────────────────────────────────────────────────────┤
│        │  Dashboard                                    ● Live · 1.0 s    │
│ ▣ Dash │ ┌──────────── CPU ────────────┐ ┌──────────── GPU ────────────┐ │
│ 〜 Graphs│ │ 54 °C     23 %     4.8 GHz  │ │ 41 °C      6 %    2.5 GHz   │ │
│ ⓘ Info │ │ Temp      Usage    Clock     │ │ Temp      Usage    Clock    │ │
│ ⚙ Set  │ │ ▁▂▃▅▃▂▁▂▃ (usage sparkline)  │ │ ▁▁▂▁▁▂▁▁▁                   │ │
│        │ │ Intel Core i7-14700          │ │ NVIDIA GeForce RTX 4070 S.  │ │
│        │ └──────────────────────────────┘ └─────────────────────────────┘ │
│        │ ┌──── Memory ────┐ ┌──── VRAM ────┐ ┌──── Fans ──────────────┐  │
│        │ │ 14.2 / 32 GB   │ │ 2.1 / 12 GB  │ │ CPU Fan     1180 RPM   │  │
│        │ │ ████░░░░ 44 %  │ │ ██░░░░ 17 %  │ │ GPU Fan     Not supp.  │  │
│        │ │ 32 GB DDR5     │ │ RTX 4070 S.  │ │ Chassis #1   740 RPM   │  │
│        │ └────────────────┘ └──────────────┘ └────────────────────────┘  │
│        │ ┌──── Storage ───────────────────────────────────────────────┐   │
│        │ │ Samsung 990 PRO 2TB      38 °C                              │   │
│        │ │ WD Blue SN570 1TB        Not supported                      │   │
│        │ └────────────────────────────────────────────────────────────┘   │
└────────┴─────────────────────────────────────────────────────────────────┘
```

- Palette: background `#0B0E14`, surface `#121722`, raised `#18202D`, border `#232C3B`,
  text `#E6EAF2`, muted `#8A94A6`, accent blue `#3B82F6` / glow `#60A5FA`,
  warn `#F59E0B`, hot `#EF4444`, unsupported `#4B5563`.
- Temperatures tint from blue → amber → red using configurable thresholds.
- Every card's footer shows the detected device name (e.g. `Intel Core i7-14700`).
- Pages: **Dashboard**, **Graphs** (CPU/GPU temp & usage, RAM, over 1–10 min),
  **Hardware** (full detected component list + every raw sensor, with supported state),
  **Settings** (refresh interval, °C/°F, graph history length, temperature thresholds,
  minimise to tray, start minimised, always on top).

## Build stages

1. ✅ **Foundation** — solution, Core sensor layer, Probe tool; verify readings on this PC.
2. ✅ **Shell + Dashboard** — theme, window chrome, navigation, live metric cards.
3. ✅ **Graphs** — lightweight live line graphs + history buffers.
4. ✅ **Hardware info page** — component details and full raw sensor table.
5. ✅ **Settings** — persisted JSON settings, units, thresholds, tray behaviour.
6. ✅ **Release** — app icon, self-contained single-file `win-x64` build, installer, README.

Each stage ends with a build, tests, and a run on real hardware.

## Notes / risks

- CPU package temperature and motherboard fan headers need **administrator** rights
  (kernel driver access). The app manifest requests elevation; without it those readings
  show `Not supported (requires administrator)`.
- Some laptops/OEM boards don't expose fans through SuperIO — shown as unsupported.
- NVMe temperatures work without extra drivers; some USB/RAID drives don't report SMART.
