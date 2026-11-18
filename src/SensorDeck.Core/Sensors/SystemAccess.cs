using System.Security.Principal;
using SensorDeck.Core.Models;

namespace SensorDeck.Core.Sensors;

/// <summary>Detects whether low-level sensor access (admin rights + PawnIO driver) is available.</summary>
public static class SystemAccess
{
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    /// <summary>
    /// LibreHardwareMonitor 0.9.5+ reads CPU temperatures, CPU clocks and motherboard fan
    /// headers through the signed PawnIO kernel driver, which is installed separately.
    /// </summary>
    public static bool IsDriverInstalled()
    {
        try { return LibreHardwareMonitor.PawnIo.PawnIo.IsInstalled; }
        catch { return false; }
    }

    public static MonitorStatus Detect() => new(IsElevated(), IsDriverInstalled());
}
