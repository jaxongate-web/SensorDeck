using System.Diagnostics;
using System.Security;
using System.Security.Principal;

namespace SensorDeck.Core.Settings;

/// <summary>
/// "Start with Windows" for an app that needs administrator rights. Windows silently blocks elevated
/// apps in the Run registry key, so this uses a Task Scheduler logon task with "highest privileges",
/// scoped to the current user, with no battery or time limits.
/// </summary>
public static class StartupTask
{
    public const string TaskName = "SensorDeck";

    public static bool IsEnabled()
    {
        var (code, _) = RunSchtasks($"/Query /TN \"{TaskName}\"");
        return code == 0;
    }

    /// <summary>Creates or replaces the logon task. Returns null on success, or an error message.</summary>
    public static string? Enable(string exePath)
    {
        string user = WindowsIdentity.GetCurrent().Name;
        string xmlPath = Path.Combine(Path.GetTempPath(), $"SensorDeck-task-{Guid.NewGuid():N}.xml");
        try
        {
            // Task Scheduler expects UTF-16 XML.
            File.WriteAllText(xmlPath, BuildXml(exePath, user), System.Text.Encoding.Unicode);
            var (code, output) = RunSchtasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
            return code == 0 ? null : output.Trim();
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }
    }

    public static string? Disable()
    {
        if (!IsEnabled()) return null;
        var (code, output) = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
        return code == 0 ? null : output.Trim();
    }

    public static string BuildXml(string exePath, string userId)
    {
        string exe = SecurityElement.Escape(exePath);
        string user = SecurityElement.Escape(userId);
        string dir = SecurityElement.Escape(Path.GetDirectoryName(exePath) ?? "");
        return $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts SensorDeck hardware monitor when you sign in.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{user}</UserId>
                  <Delay>PT10S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{user}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <AllowHardTerminate>true</AllowHardTerminate>
                <StartWhenAvailable>false</StartWhenAvailable>
                <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
                <IdleSettings>
                  <StopOnIdleEnd>false</StopOnIdleEnd>
                  <RestartOnIdle>false</RestartOnIdle>
                </IdleSettings>
                <AllowStartOnDemand>true</AllowStartOnDemand>
                <Enabled>true</Enabled>
                <Hidden>false</Hidden>
                <RunOnlyIfIdle>false</RunOnlyIfIdle>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>"{exe}"</Command>
                  <Arguments>--minimized</Arguments>
                  <WorkingDirectory>{dir}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;
    }

    private static (int Code, string Output) RunSchtasks(string args)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = Process.Start(psi)!;
        string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(10_000);
        return (p.ExitCode, output);
    }
}
