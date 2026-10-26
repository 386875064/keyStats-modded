using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace KeyStats.Services;

public class StartupManager
{
    private static StartupManager? _instance;
    public static StartupManager Instance => _instance ??= new StartupManager();

    private const string TaskName = "KeyStats";
    private const string LegacyRegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyAppName = "KeyStats";

    private StartupManager() { }

    /// <summary>
    /// 计划任务是否存在（开机自启 = 计划任务 ONLOGON + 最高权限）。
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                return RunScheduler($"/Query /TN \"{TaskName}\"");
            }
            catch
            {
                return false;
            }
        }
    }

    public void SyncWithSettings()
    {
        try
        {
            // HKCU Run 项 + RUNASADMIN 兼容层在登录场景会被 Windows 静默丢弃（Explorer 非提权令牌
            // 拉不起需要提升的 Run 项），改用计划任务后旧 Run 项只会造成双启动，清掉。
            DeleteLegacyRunEntry();

            if (!StatsManager.Instance.Settings.LaunchAtStartup)
            {
                return;
            }

            // 幂等重建（/F 覆盖），保证任务指向的 exe 路径永远与当前一致
            SetEnabled(true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error syncing startup: {ex.Message}");
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled)
        {
            var exePath = GetCurrentExecutablePath();
            if (string.IsNullOrWhiteSpace(exePath))
            {
                throw new InvalidOperationException("无法确定当前程序路径，无法设置开机启动。");
            }

            if (!RunScheduler($"/Create /F /TN \"{TaskName}\" /TR \"\\\"{exePath}\\\"\" /SC ONLOGON /RL HIGHEST"))
            {
                throw new InvalidOperationException("创建开机启动计划任务失败（需要管理员权限）。");
            }
        }
        else
        {
            RunScheduler($"/Delete /F /TN \"{TaskName}\"");
            DeleteLegacyRunEntry();
        }

        StatsManager.Instance.Settings.LaunchAtStartup = enabled;
        StatsManager.Instance.SaveSettings();
    }

    private static bool RunScheduler(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            if (process == null)
            {
                return false;
            }

            if (!process.WaitForExit(10_000))
            {
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"schtasks failed: {arguments} -> {ex.Message}");
            return false;
        }
    }

    private static void DeleteLegacyRunEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(LegacyRegistryKeyPath, true);
            key?.DeleteValue(LegacyAppName, false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to remove legacy run entry: {ex.Message}");
        }
    }

    private static string? GetCurrentExecutablePath()
    {
        try
        {
            var exePath = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                return Path.GetFullPath(exePath);
            }
        }
        catch
        {
        }

        try
        {
            var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrWhiteSpace(exePath))
            {
                return Path.GetFullPath(exePath);
            }
        }
        catch
        {
        }

        return null;
    }
}
