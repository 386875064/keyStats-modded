using System;
using System.Diagnostics;
using System.Security.Principal;

namespace KeyStats.Helpers;

/// <summary>
/// Administrator-rights helpers. Running elevated is not required, but it makes the
/// low-level input hooks reliable when a game (or its anti-cheat) runs elevated too.
/// </summary>
public static class ElevationHelper
{
    public static bool IsRunningElevated()
    {
        try
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Relaunches the current executable with the "runas" verb.
    /// Returns false when the user dismisses the UAC prompt or the launch fails.
    /// </summary>
    public static bool TryRestartElevated()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
