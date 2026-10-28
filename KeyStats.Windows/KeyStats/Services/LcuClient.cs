using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KeyStats.Services;

/// <summary>
/// 只读 LCU 本地接口（不注入、不读内存、不改客户端），负责两件事：
/// 1) 选人阶段抓「我选了谁」→ 开局时先记下；
/// 2) 对局结束后再抓一次（结算页 / 对局历史）覆盖；同时下载官方头像缓存到本地。
/// 资源策略：端点发现带 30 秒冷却；请求全部 3 秒超时；失败不重试刷屏。
/// </summary>
internal sealed class LcuClient : IDisposable
{
    private const int RequestTimeoutMs = 3000;
    private static readonly TimeSpan DiscoverRetryInterval = TimeSpan.FromSeconds(30);

    private static readonly string[] ExtraLockfilePaths =
    {
        @"D:\wegameapps\英雄联盟\LeagueClient\lockfile",
        @"C:\Program Files\WeGame\英雄联盟\LeagueClient\lockfile",
        @"C:\Riot Games\League of Legends\lockfile",
    };

    private readonly HttpClient _http;
    private readonly object _endpointLock = new();
    private (string BaseUrl, string Token)? _endpoint;
    private DateTime _lastDiscoverAttemptUtc = DateTime.MinValue;
    private Dictionary<int, string>? _championNames;

    public LcuClient()
    {
        var handler = new HttpClientHandler
        {
            // LCU 使用本地自签证书
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(RequestTimeoutMs) };
    }

    // ===== 端点发现 =====

    private (string BaseUrl, string Token)? GetEndpoint(bool force = false)
    {
        lock (_endpointLock)
        {
            if (!force && _endpoint != null)
            {
                return _endpoint;
            }

            if (!force && DateTime.UtcNow - _lastDiscoverAttemptUtc < DiscoverRetryInterval)
            {
                return null;
            }

            _lastDiscoverAttemptUtc = DateTime.UtcNow;
            _endpoint = Discover();
            return _endpoint;
        }
    }

    private void InvalidateEndpoint()
    {
        lock (_endpointLock)
        {
            _endpoint = null;
        }
    }

    /// <summary>lockfile 优先（客户端运行时才有内容），目录从 LeagueClientUx 进程推导，再兜底常见路径。</summary>
    private static (string BaseUrl, string Token)? Discover()
    {
        foreach (var path in EnumerateLockfilePaths())
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var line = File.ReadAllText(path, Encoding.UTF8).Trim();
                var parts = line.Split(':');
                if (parts.Length < 5 || parts[2].Length == 0 || parts[3].Length == 0)
                {
                    continue;
                }

                return ($"https://127.0.0.1:{parts[2]}", parts[3]);
            }
            catch
            {
                // 读不到就试下一个
            }
        }

        return null;
    }

    private static List<string> EnumerateLockfilePaths()
    {
        var paths = new List<string>();
        try
        {
            foreach (var proc in Process.GetProcessesByName("LeagueClientUx"))
            {
                try
                {
                    var dir = Path.GetDirectoryName(proc.MainModule?.FileName);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        paths.Add(Path.Combine(dir, "lockfile"));
                    }
                }
                catch
                {
                    // 某些情况下读不到模块路径，忽略
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            // 进程枚举失败就只走兜底路径
        }

        paths.AddRange(ExtraLockfilePaths);
        return paths;
    }

    // ===== 1) 选人阶段：抓「我选了谁」 =====

    /// <summary>
    /// 读选人会话里的自己的 championId（还没选好返回 0）。
    /// my-selection 最直接；champ-select session 用 localPlayerCellId 兜底。
    /// </summary>
    public async Task<int> TryGetChampSelectChampionIdAsync()
    {
        var endpoint = GetEndpoint();
        if (endpoint == null)
        {
            return 0;
        }

        using (var selection = await GetJsonAsync(endpoint.Value, "/lol-champ-select/v1/session/my-selection").ConfigureAwait(false))
        {
            var id = TryInt(selection?.RootElement, "championId");
            if (id > 0)
            {
                return id;
            }
        }

        using var session = await GetJsonAsync(endpoint.Value, "/lol-champ-select/v1/session").ConfigureAwait(false);
        if (session == null)
        {
            return 0;
        }

        var root = session.RootElement;
        var myCell = TryInt(root, "localPlayerCellId");
        if (myCell < 0 || !root.TryGetProperty("myTeam", out var myTeam) || myTeam.ValueKind != JsonValueKind.Array)
        {
            return 0;
        }

        foreach (var player in myTeam.EnumerateArray())
        {
            if (TryInt(player, "cellId") == myCell)
            {
                return TryInt(player, "championId");
            }
        }

        return 0;
    }

    // ===== 2) 结算后：覆盖补录 + 头像 =====

    /// <summary>
    /// 结算后解析本局英雄：先用结算页数据，再用对局历史兜底。
    /// 返回 (championId, 中文名)；失败返回 (0, "")。
    /// </summary>
    public async Task<(int ChampionId, string ChampionName)> ResolveLastGameChampionAsync()
    {
        var endpoint = GetEndpoint();
        if (endpoint == null)
        {
            return (0, string.Empty);
        }

        var myPuuid = await TryGetMyPuuidAsync(endpoint.Value);

        var championId = await TryChampionFromEogAsync(endpoint.Value, myPuuid);
        if (championId <= 0)
        {
            championId = await TryChampionFromMatchHistoryAsync(endpoint.Value, myPuuid);
        }

        if (championId <= 0)
        {
            return (0, string.Empty);
        }

        var name = await TryGetChampionNameAsync(endpoint.Value, championId);
        return (championId, name ?? string.Empty);
    }

    /// <summary>下载官方英雄头像（LCU 静态资源）到本地缓存；成功后离线也能显示。</summary>
    public async Task<bool> TryDownloadChampionIconAsync(int championId, string savePath)
    {
        if (championId <= 0 || string.IsNullOrWhiteSpace(savePath))
        {
            return false;
        }

        try
        {
            if (File.Exists(savePath) && new FileInfo(savePath).Length > 100)
            {
                return true;
            }
        }
        catch
        {
            // 忽略，继续下载
        }

        var endpoint = GetEndpoint();
        if (endpoint == null)
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get,
                endpoint.Value.BaseUrl + $"/lol-game-data/assets/v1/champion-icons/{championId}.png");
            request.Headers.Authorization = BuildAuth(endpoint.Value.Token);
            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (bytes.Length < 100)
            {
                return false;
            }

            var dir = Path.GetDirectoryName(savePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllBytes(savePath, bytes);
            return true;
        }
        catch
        {
            InvalidateEndpoint();
            return false;
        }
    }

    // ===== HTTP 基础 =====

    private static System.Net.Http.Headers.AuthenticationHeaderValue BuildAuth(string token)
    {
        return new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes("riot:" + token)));
    }

    private Task<JsonDocument?> GetJsonAsync((string BaseUrl, string Token) endpoint, string path)
    {
        return GetJsonAsync(_http, endpoint, path);
    }

    private static async Task<JsonDocument?> GetJsonAsync(
        HttpClient http, (string BaseUrl, string Token) endpoint, string path)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.BaseUrl + path);
            request.Headers.Authorization = BuildAuth(endpoint.Token);
            using var response = await http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(json);
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> TryGetMyPuuidAsync((string BaseUrl, string Token) endpoint)
    {
        using var doc = await GetJsonAsync(endpoint, "/lol-summoner/v1/current-summoner").ConfigureAwait(false);
        return TryString(doc?.RootElement, "puuid") ?? string.Empty;
    }

    /// <summary>结算页数据：找 localPlayer 的 championId，其次按自己的 puuid 匹配。</summary>
    private async Task<int> TryChampionFromEogAsync((string BaseUrl, string Token) endpoint, string myPuuid)
    {
        using var doc = await GetJsonAsync(endpoint, "/lol-end-of-game/v1/eog-stats-block").ConfigureAwait(false);
        if (doc == null)
        {
            return 0;
        }

        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("localPlayer", out var localPlayer))
        {
            var id = TryInt(localPlayer, "championId");
            if (id > 0)
            {
                return id;
            }
        }

        if (!string.IsNullOrEmpty(myPuuid))
        {
            var matched = FindChampionIdByPuuid(root, myPuuid);
            if (matched > 0)
            {
                return matched;
            }
        }

        return 0;
    }

    /// <summary>对局历史：取最近一局，按 puuid 找自己的 championId。</summary>
    private async Task<int> TryChampionFromMatchHistoryAsync((string BaseUrl, string Token) endpoint, string myPuuid)
    {
        using var doc = await GetJsonAsync(endpoint,
            "/lol-match-history/v1/products/lol/current-summoner/matches?begIndex=0&endIndex=1").ConfigureAwait(false);
        if (doc == null)
        {
            return 0;
        }

        if (!doc.RootElement.TryGetProperty("games", out var games) ||
            games.ValueKind != JsonValueKind.Array ||
            games.GetArrayLength() == 0)
        {
            return 0;
        }

        var game = games[0];
        if (!string.IsNullOrEmpty(myPuuid))
        {
            var matched = FindChampionIdByPuuid(game, myPuuid);
            if (matched > 0)
            {
                return matched;
            }
        }

        // 兜底：第一个带 championId 的 participant
        if (game.TryGetProperty("participants", out var participants) &&
            participants.ValueKind == JsonValueKind.Array)
        {
            foreach (var participant in participants.EnumerateArray())
            {
                var id = TryInt(participant, "championId");
                if (id > 0)
                {
                    return id;
                }
            }
        }

        return 0;
    }

    /// <summary>在任意 JSON 结构里找「puuid 匹配节点」的 championId（深度遍历）。</summary>
    private static int FindChampionIdByPuuid(JsonElement element, string puuid)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                if (element.TryGetProperty("puuid", out var p) && p.ValueKind == JsonValueKind.String &&
                    string.Equals(p.GetString(), puuid, StringComparison.OrdinalIgnoreCase))
                {
                    var id = TryInt(element, "championId");
                    if (id > 0)
                    {
                        return id;
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    var found = FindChampionIdByPuuid(property.Value, puuid);
                    if (found > 0)
                    {
                        return found;
                    }
                }
                return 0;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var found = FindChampionIdByPuuid(item, puuid);
                    if (found > 0)
                    {
                        return found;
                    }
                }
                return 0;

            default:
                return 0;
        }
    }

    /// <summary>championId → 中文名（客户端资源，跟随客户端语言，一次缓存）。</summary>
    public async Task<string?> TryGetChampionNameAsync(int championId)
    {
        var endpoint = GetEndpoint();
        if (endpoint == null || championId <= 0)
        {
            return null;
        }

        return await TryGetChampionNameAsync(endpoint.Value, championId).ConfigureAwait(false);
    }

    private async Task<string?> TryGetChampionNameAsync((string BaseUrl, string Token) endpoint, int championId)
    {
        if (_championNames == null)
        {
            using var doc = await GetJsonAsync(endpoint,
                "/lol-game-data/assets/v1/champion-summary.json").ConfigureAwait(false);
            if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var map = new Dictionary<int, string>();
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var id = TryInt(item, "id");
                var name = TryString(item, "name");
                if (id > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    map[id] = name!;
                }
            }

            _championNames = map;
        }

        return _championNames.TryGetValue(championId, out var n) ? n : null;
    }

    // ===== JSON 小工具 =====

    private static int TryInt(JsonElement? element, string property)
    {
        if (element is not { ValueKind: JsonValueKind.Object } obj || !obj.TryGetProperty(property, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out var parsed))
        {
            return parsed;
        }

        return 0;
    }

    private static string? TryString(JsonElement? element, string property)
    {
        if (element is not { ValueKind: JsonValueKind.Object } obj ||
            !obj.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
