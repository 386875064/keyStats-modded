using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;

namespace KeyStats.Services;

/// <summary>
/// 每日数据备份：把 4 个关键 JSON 打包到数据目录的兄弟目录（避免整个 KeyStats 目录被误删时连备份一起没），
/// 保留最近 7 份。启动时检查一次，当天已备份则跳过。
/// </summary>
internal static class BackupService
{
    private const int KeepCount = 7;
    private const string BackupFolderName = "KeyStats-Backups";
    private static readonly string[] SourceFiles =
    {
        "daily_stats.json",
        "history.json",
        "game_sessions.json",
        "settings.json"
    };

    public static string BackupFolder => Path.Combine(
        Path.GetDirectoryName(StatsManager.Instance.DataFolder) ?? StatsManager.Instance.DataFolder,
        BackupFolderName);

    /// <summary>当天还没备份就打包一次；失败静默（不影响主流程）。建议在后台线程调用。</summary>
    public static void TryRunDailyBackup()
    {
        try
        {
            var dataFolder = StatsManager.Instance.DataFolder;
            var folder = BackupFolder;
            Directory.CreateDirectory(folder);

            var target = Path.Combine(folder, $"KeyStats-backup-{DateTime.Now:yyyyMMdd}.zip");
            if (File.Exists(target))
            {
                return;
            }

            var files = SourceFiles
                .Select(name => Path.Combine(dataFolder, name))
                .Where(File.Exists)
                .ToList();
            if (files.Count == 0)
            {
                return;
            }

            using (var archive = ZipFile.Open(target, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    archive.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
                }
            }

            // 轮转：只留最近 KeepCount 份
            var stale = Directory.GetFiles(folder, "KeyStats-backup-*.zip")
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .Skip(KeepCount)
                .ToList();
            foreach (var path in stale)
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // 删不掉就算了，下次再说
                }
            }

            Debug.WriteLine($"Daily backup written: {target} ({files.Count} files, removed {stale.Count} old)");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Daily backup failed: {ex.Message}");
        }
    }

    /// <summary>启动时后台触发（不阻塞启动路径）。</summary>
    public static void ScheduleOnStartup()
    {
        _ = Task.Run(TryRunDailyBackup);
    }
}
