using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KeyStats.Services;

/// <summary>
/// 每日数据备份：优先上传到 BOSS 的 Supabase（坑货雷达项目），云端失败时降级写本地 zip。
/// 云端表 keystats_backups 见仓库 db/keystats_backups.sql；按 (team, device_id, backup_date) 幂等覆盖。
/// </summary>
internal static class BackupService
{
    private const string SupabaseUrl = "https://nvlafhkjgshacesdimky.supabase.co";
    private const string SupabasePublishableKey = "sb_publishable_o6q0Sfhxbmx3cc1T_NWF0A_4ACBvTWw";
    private const string TeamValue = "386875064";
    private const string Table = "keystats_backups";
    private const int KeepLocalCount = 7;

    private static readonly string[] SourceFiles =
    {
        "daily_stats.json",
        "history.json",
        "game_sessions.json",
        "settings.json"
    };

    /// <summary>最近一次备份结果（报告窗直接显示）。</summary>
    public static string LastStatus { get; private set; } = "尚未运行";

    public static string BackupFolder => Path.Combine(
        Path.GetDirectoryName(StatsManager.Instance.DataFolder) ?? StatsManager.Instance.DataFolder,
        "KeyStats-Backups");

    public static void TryRunDailyBackup()
    {
        try
        {
            var dataFolder = StatsManager.Instance.DataFolder;
            var files = SourceFiles
                .Select(name => Path.Combine(dataFolder, name))
                .Where(File.Exists)
                .ToList();
            if (files.Count == 0)
            {
                LastStatus = "没有可备份的数据文件";
                return;
            }

            var payload = new Dictionary<string, string>();
            foreach (var file in files)
            {
                payload[Path.GetFileName(file)] = File.ReadAllText(file, Encoding.UTF8);
            }

            var json = JsonSerializer.Serialize(payload);
            var compressed = Gzip(Encoding.UTF8.GetBytes(json));
            var base64 = Convert.ToBase64String(compressed);

            var row = new Dictionary<string, object>
            {
                ["team"] = TeamValue,
                ["device_id"] = Environment.MachineName,
                ["backup_date"] = DateTime.Now.ToString("yyyy-MM-dd"),
                ["payload_gzip_base64"] = base64,
                ["app_version"] = typeof(BackupService).Assembly.GetName().Version?.ToString() ?? string.Empty
            };

            var uploaded = UploadAsync(row).GetAwaiter().GetResult();
            if (uploaded)
            {
                LastStatus = $"云端已备份 · {DateTime.Now:MM-dd HH:mm} · {base64.Length / 1024} KB";
                Debug.WriteLine($"Backup uploaded to Supabase ({base64.Length / 1024} KB, {files.Count} files)");
                return;
            }

            WriteLocalZip(files);
            LastStatus = $"云端上传失败，已存本地备份 · {DateTime.Now:MM-dd HH:mm}";
        }
        catch (Exception ex)
        {
            LastStatus = "备份失败：" + ex.Message;
            Debug.WriteLine($"Daily backup failed: {ex.Message}");
        }
    }

    public static void ScheduleOnStartup()
    {
        _ = Task.Run(TryRunDailyBackup);
    }

    private static async Task<bool> UploadAsync(Dictionary<string, object> row)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            http.DefaultRequestHeaders.Add("apikey", SupabasePublishableKey);
            http.DefaultRequestHeaders.Add("Authorization", "Bearer " + SupabasePublishableKey);
            http.DefaultRequestHeaders.Add("Prefer", "resolution=merge-duplicates,return=minimal");

            var body = JsonSerializer.Serialize(new[] { row });
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            var url = $"{SupabaseUrl}/rest/v1/{Table}?on_conflict=team,device_id,backup_date";
            using var response = await http.PostAsync(url, content).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            Debug.WriteLine($"Backup upload rejected: {(int)response.StatusCode} {text}");
            return false;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Backup upload error: {ex.Message}");
            return false;
        }
    }

    private static byte[] Gzip(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(data, 0, data.Length);
        }

        return output.ToArray();
    }

    /// <summary>云端不可用时的兜底：本地 zip，保留最近 7 份。</summary>
    private static void WriteLocalZip(List<string> files)
    {
        try
        {
            var folder = BackupFolder;
            Directory.CreateDirectory(folder);

            var target = Path.Combine(folder, $"KeyStats-backup-{DateTime.Now:yyyyMMdd}.zip");
            using (var archive = ZipFile.Open(target, ZipArchiveMode.Create))
            {
                foreach (var file in files)
                {
                    archive.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.Optimal);
                }
            }

            var stale = Directory.GetFiles(folder, "KeyStats-backup-*.zip")
                .OrderByDescending(path => path, StringComparer.Ordinal)
                .Skip(KeepLocalCount)
                .ToList();
            foreach (var path in stale)
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // 删不掉就留着
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Local fallback backup failed: {ex.Message}");
        }
    }
}
