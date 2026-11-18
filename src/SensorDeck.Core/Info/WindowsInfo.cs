using System.Management;
using Microsoft.Win32;

namespace SensorDeck.Core.Info;

/// <summary>Physical drive details from Windows' storage subsystem (MSFT_PhysicalDisk).</summary>
public sealed record DriveDetails(string Name, ulong SizeBytes, string Interface, string MediaType)
{
    /// <summary>e.g. "NVMe SSD · 2.0 TB".</summary>
    public string Summary
    {
        get
        {
            var kind = MediaType == "Unspecified" ? Interface : $"{Interface} {MediaType}";
            return SizeBytes > 0 ? $"{kind} · {ByteFormat.Bytes(SizeBytes)}" : kind;
        }
    }
}

public sealed record GpuDriverInfo(string Name, string Version, DateTime? Date);

/// <summary>
/// Static facts Windows already knows (drive capacity/interface, GPU driver, OS edition).
/// Every query is best-effort: if WMI is unavailable the information is simply omitted.
/// </summary>
public static class WindowsInfo
{
    public static IReadOnlyList<DriveDetails> ReadDrives()
    {
        var list = new List<DriveDetails>();
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage",
                "SELECT FriendlyName, Size, BusType, MediaType FROM MSFT_PhysicalDisk");
            foreach (ManagementObject o in searcher.Get())
            {
                using (o)
                {
                    list.Add(new DriveDetails(
                        (o["FriendlyName"] as string ?? "").Trim(),
                        o["Size"] is ulong size ? size : 0,
                        BusTypeName(Convert.ToInt32(o["BusType"] ?? 0)),
                        MediaTypeName(Convert.ToInt32(o["MediaType"] ?? 0))));
                }
            }
        }
        catch { /* WMI unavailable — no extra drive details */ }
        return list;
    }

    public static IReadOnlyList<GpuDriverInfo> ReadGpuDrivers()
    {
        var list = new List<GpuDriverInfo>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, DriverVersion, DriverDate FROM Win32_VideoController");
            foreach (ManagementObject o in searcher.Get())
            {
                using (o)
                {
                    DateTime? date = null;
                    if (o["DriverDate"] is string d && d.Length >= 8)
                        try { date = ManagementDateTimeConverter.ToDateTime(d); } catch { }
                    list.Add(new GpuDriverInfo((o["Name"] as string ?? "").Trim(), (o["DriverVersion"] as string ?? "").Trim(), date));
                }
            }
        }
        catch { /* WMI unavailable */ }
        return list;
    }

    /// <summary>"Windows 11 Home 25H2 (build 26200.9711)". The registry still says "Windows 10" on Windows 11.</summary>
    public static string ReadOsName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            string product = key?.GetValue("ProductName") as string ?? "Windows";
            string? display = key?.GetValue("DisplayVersion") as string;
            string build = key?.GetValue("CurrentBuild") as string ?? "";
            object? ubr = key?.GetValue("UBR");
            if (int.TryParse(build, out int b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11");
            string version = display is null ? "" : $" {display}";
            string fullBuild = ubr is null ? build : $"{build}.{ubr}";
            return $"{product}{version} (build {fullBuild})";
        }
        catch
        {
            return System.Runtime.InteropServices.RuntimeInformation.OSDescription;
        }
    }

    /// <summary>NVIDIA's familiar version: Windows "32.0.15.9186" → "591.86".</summary>
    public static string FriendlyDriverVersion(string gpuName, string version)
    {
        if (!gpuName.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return version;
        var parts = version.Split('.');
        if (parts.Length < 4) return version;
        string digits = parts[2] + parts[3];
        if (digits.Length < 5 || !digits.All(char.IsDigit)) return version;
        string last5 = digits[^5..];
        return $"{last5[..3]}.{last5[3..]}";
    }

    internal static string BusTypeName(int bus) => bus switch
    {
        1 => "SCSI", 3 => "ATA", 7 => "USB", 8 => "RAID", 10 => "SAS", 11 => "SATA",
        12 => "SD", 13 => "MMC", 15 => "File-backed", 16 => "Storage Spaces", 17 => "NVMe",
        _ => "Drive",
    };

    internal static string MediaTypeName(int media) => media switch
    {
        3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Unspecified",
    };
}

public static class ByteFormat
{
    /// <summary>Decimal units, matching how drive capacities are sold (2,000,398,934,016 B → "2.0 TB").</summary>
    public static string Bytes(ulong bytes) => bytes switch
    {
        >= 1_000_000_000_000 => $"{bytes / 1e12:0.0} TB",
        >= 1_000_000_000 => $"{bytes / 1e9:0} GB",
        >= 1_000_000 => $"{bytes / 1e6:0} MB",
        _ => $"{bytes} B",
    };
}
