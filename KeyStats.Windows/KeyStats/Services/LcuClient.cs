using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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

    // ddragon 官方静态 CDN（LCU 不在线时的英雄目录/头像兜底源，国内可直连，2026-10-28 实测）
    private static readonly HttpClient CdnHttp = CreateCdnHttp();
    private string? _ddVersion;
    private List<ChampionCatalogEntry>? _catalog;
    private string CatalogFilePath => Path.Combine(StatsManager.Instance.DataFolder, "champion-catalog.json");

    private static HttpClient CreateCdnHttp()
    {
        var handler = new HttpClientHandler
        {
            // 某些网络环境对 Riot CDN 证书链校验不稳，静态资源只读、无敏感数据，放行
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
    }

    public LcuClient()
    {
        var handler = new HttpClientHandler
        {
            // LCU 使用本地自签证书
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(RequestTimeoutMs) };
    }

    /// <summary>诊断日志：%LOCALAPPDATA%\KeyStats\lcu.log（超 128KB 重建）。</summary>
    public static void WriteDiag(string message)
    {
        try
        {
            var folder = StatsManager.Instance.DataFolder;
            var path = Path.Combine(folder, "lcu.log");
            var line = DateTime.Now.ToString("MM-dd HH:mm:ss.fff") + " " + message;
            lock (DiagLock)
            {
                Directory.CreateDirectory(folder);
                if (File.Exists(path) && new FileInfo(path).Length > 128 * 1024)
                {
                    File.WriteAllText(path, string.Empty);
                }
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
            // 诊断日志写不进就算了
        }
    }

    private static readonly object DiagLock = new();
    private static DateTime _lastDiscoverFailLogUtc = DateTime.MinValue;
    private static string _lastLoggedPort = string.Empty;
    private static string _lastLoggedEndpoint = string.Empty;
    private static bool _lastClientRunning = true;

    /// <summary>LCU 客户端进程名（国服 WeGame 与国际服都跑这两个之一）。</summary>
    private static readonly string[] ClientProcessNames = { "LeagueClientUx", "LeagueClient" };

    /// <summary>
    /// 客户端进程是否在运行。客户端没开时 lockfile 必是空/死文件、命令行也读不到，
    /// 每 30 秒空跑一次没有意义（2026-11-26 BOSS 要求：有客户端了再探测）。
    /// 进程枚举失败时返回 true，宁可多试一次也不漏捕获。
    /// </summary>
    private static bool IsClientProcessRunning()
    {
        foreach (var processName in ClientProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch
            {
                return true;
            }

            try
            {
                if (processes.Length > 0)
                {
                    return true;
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        return false;
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

    /// <summary>
    /// 发现 LCU 端点：① 标准 lockfile（Riot 国际服安装）→
    /// ② LeagueClientUx 进程命令行（WeGame/国服客户端不写 lockfile，只能读启动参数里的
    ///    --app-port / --remoting-auth-token —— 2026-10-31 实测国服 lockfile 是 0 字节死文件）。
    /// </summary>
    private static (string BaseUrl, string Token)? Discover()
    {
        // 客户端进程不在 → lockfile 必然是 0 字节死文件、命令行也取不到，直接跳过，
        // 不再每 30 秒白跑一次。客户端启动到进入选人界面有十几秒窗口，不会漏选人捕获。
        if (!IsClientProcessRunning())
        {
            if (_lastClientRunning)
            {
                _lastClientRunning = false;
                WriteDiag("客户端未运行，暂停 LCU 探测（进程出现后自动恢复）");
            }

            return null;
        }

        if (!_lastClientRunning)
        {
            _lastClientRunning = true;
            WriteDiag("检测到客户端进程，恢复 LCU 探测");
        }

        var candidates = EnumerateLockfilePaths();
        foreach (var path in candidates)
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

                var port = parts[2];
                if (port != _lastLoggedPort)
                {
                    _lastLoggedPort = port;
                    WriteDiag("端点发现成功 port=" + port + " 来源=lockfile " + path);
                }

                return ($"https://127.0.0.1:{port}", parts[3]);
            }
            catch
            {
                // 读不到就试下一个
            }
        }

        var fromCommandLine = DiscoverFromCommandLine();
        if (fromCommandLine is { } endpoint)
        {
            if (endpoint.BaseUrl != _lastLoggedEndpoint)
            {
                _lastLoggedEndpoint = endpoint.BaseUrl;
                WriteDiag("端点发现成功 " + endpoint.BaseUrl + " 来源=进程命令行");
            }

            return endpoint;
        }

        // 失败日志 10 分钟最多一条，避免刷屏
        if (DateTime.UtcNow - _lastDiscoverFailLogUtc > TimeSpan.FromMinutes(10))
        {
            _lastDiscoverFailLogUtc = DateTime.UtcNow;
            var processCount = 0;
            try
            {
                processCount = Process.GetProcessesByName("LeagueClientUx").Length;
            }
            catch
            {
                // 忽略
            }

            WriteDiag("端点发现失败：lockfile " + candidates.Count + " 条（"
                      + DescribeCandidates(candidates) + "），LeagueClientUx 进程 "
                      + processCount + " 个，命令行没取到端口/token");
        }

        return null;
    }

    private static string DescribeCandidates(List<string> candidates)
    {
        var parts = new List<string>();
        foreach (var path in candidates.Take(4))
        {
            var leaf = Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty)
                       + "/" + Path.GetFileName(path);
            string state;
            try
            {
                state = File.Exists(path) ? new FileInfo(path).Length + "B" : "无";
            }
            catch
            {
                state = "?";
            }

            parts.Add(leaf + "=" + state);
        }

        return string.Join(", ", parts);
    }

    /// <summary>从 LeagueClientUx（其次 LeagueClient）命令行读取 --app-port / --remoting-auth-token。</summary>
    private static (string BaseUrl, string Token)? DiscoverFromCommandLine()
    {
        foreach (var processName in new[] { "LeagueClientUx", "LeagueClient" })
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(processName);
            }
            catch
            {
                continue;
            }

            try
            {
                foreach (var process in processes)
                {
                    try
                    {
                        var commandLine = TryGetCommandLine(process.Id);
                        if (string.IsNullOrEmpty(commandLine))
                        {
                            continue;
                        }

                        var port = ExtractArgument(commandLine, "--app-port");
                        var token = ExtractArgument(commandLine, "--remoting-auth-token");
                        if (!string.IsNullOrEmpty(port) && !string.IsNullOrEmpty(token))
                        {
                            return ("https://127.0.0.1:" + port, token!);
                        }
                    }
                    catch
                    {
                        // 单个进程失败不影响其它
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            finally
            {
                foreach (var process in processes)
                {
                    process.Dispose();
                }
            }
        }

        return null;
    }

    private static string? TryGetCommandLine(int processId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT CommandLine FROM Win32_Process WHERE ProcessId = " + processId);
            foreach (ManagementBaseObject result in searcher.Get())
            {
                return result["CommandLine"] as string;
            }
        }
        catch
        {
            // WMI 不可用就放弃
        }

        return null;
    }

    /// <summary>取 --name=value / --name value 形式参数的值（值可带引号）。</summary>
    private static string? ExtractArgument(string commandLine, string name)
    {
        var index = commandLine.IndexOf(name, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        var start = index + name.Length;
        if (start >= commandLine.Length)
        {
            return null;
        }

        if (commandLine[start] == '=' || commandLine[start] == ' ')
        {
            start++;
        }

        if (start < commandLine.Length && commandLine[start] == '"')
        {
            start++;
        }

        var end = start;
        while (end < commandLine.Length && commandLine[end] != ' ' && commandLine[end] != '"')
        {
            end++;
        }

        return end > start ? commandLine.Substring(start, end - start) : null;
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

    /// <summary>最近 N 局「我的英雄ID」列表（按开局时间），用于补录历史缺英雄的记录。</summary>
    public async Task<List<(DateTimeOffset Created, int ChampionId)>> GetMyRecentGameChampionsAsync(int count)
    {
        var result = new List<(DateTimeOffset, int)>();
        var endpoint = GetEndpoint();
        if (endpoint == null)
        {
            return result;
        }

        var myPuuid = await TryGetMyPuuidAsync(endpoint.Value).ConfigureAwait(false);
        var (status, body) = await GetRawAsync(endpoint.Value,
            "/lol-match-history/v1/products/lol/current-summoner/matches?begIndex=0&endIndex=" + count)
            .ConfigureAwait(false);
        WriteDiag("补录数据：HTTP " + status + " len=" + body.Length + " puuid=" +
                  (string.IsNullOrEmpty(myPuuid) ? "空" : "有"));
        if (status != 200 || string.IsNullOrWhiteSpace(body))
        {
            WriteDiag("   响应：" + Snippet(body));
            return result;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            // 注意：实际返回是 {"accountId":..,"games":{"gameCount":21,"games":[...]}}，
            // games 是对象不是数组（老客户端可能是纯数组，两种都兼容）
            var gamesArray = FindGamesArray(doc.RootElement);
            if (gamesArray == null)
            {
                WriteDiag("   无 games 数据：" + Snippet(body));
                return result;
            }

            foreach (var game in gamesArray.Value.EnumerateArray())
            {
                var created = 0L;
                if (game.TryGetProperty("gameCreation", out var createdElement) &&
                    createdElement.ValueKind == JsonValueKind.Number)
                {
                    created = createdElement.GetInt64();
                }

                if (created <= 0)
                {
                    continue;
                }

                var championId = string.IsNullOrEmpty(myPuuid) ? 0 : FindChampionIdByPuuid(game, myPuuid);
                if (championId <= 0 &&
                    game.TryGetProperty("participants", out var participants) &&
                    participants.ValueKind == JsonValueKind.Array)
                {
                    foreach (var participant in participants.EnumerateArray())
                    {
                        var id = TryInt(participant, "championId");
                        if (id > 0)
                        {
                            championId = id;
                            break;
                        }
                    }
                }

                if (championId > 0)
                {
                    result.Add((DateTimeOffset.FromUnixTimeMilliseconds(created), championId));
                }
            }
        }
        catch (Exception ex)
        {
            WriteDiag("   解析异常：" + ex.Message + " | " + Snippet(body));
        }

        if (result.Count == 0)
        {
            WriteDiag("   解析到 0 局：" + Snippet(body));
        }

        return result;
    }

    private static string Snippet(string text)
    {
        var flat = (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return flat.Length <= 300 ? flat : flat.Substring(0, 300);
    }

    /// <summary>在 match-history 响应里找对局数组：兼容 games=[...] 与 games.games=[...] 两种结构。</summary>
    private static JsonElement? FindGamesArray(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("games", out var games))
        {
            return null;
        }

        if (games.ValueKind == JsonValueKind.Array)
        {
            return games;
        }

        if (games.ValueKind == JsonValueKind.Object &&
            games.TryGetProperty("games", out var inner) &&
            inner.ValueKind == JsonValueKind.Array)
        {
            return inner;
        }

        return null;
    }

    /// <summary>原始 GET（状态码 + 正文），用于需要看响应细节的诊断场景。</summary>
    private async Task<(int Status, string Body)> GetRawAsync(
        (string BaseUrl, string Token) endpoint, string path)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.BaseUrl + path);
            request.Headers.Authorization = BuildAuth(endpoint.Token);
            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                InvalidateEndpoint();
                WriteDiag("HTTP " + (int)response.StatusCode + " " + path + "：token 失效，端点作废");
            }

            return ((int)response.StatusCode, body);
        }
        catch (Exception ex)
        {
            InvalidateEndpoint();
            WriteDiag("请求失败 " + path + "：" + ex.GetType().Name + " " + ex.Message + " → 端点作废");
            return (-1, ex.Message);
        }
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

    private async Task<JsonDocument?> GetJsonAsync((string BaseUrl, string Token) endpoint, string path)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint.BaseUrl + path);
            request.Headers.Authorization = BuildAuth(endpoint.Token);
            using var response = await _http.SendAsync(request).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // 401/403 = token 过期（客户端重启过），立刻作废端点
                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    InvalidateEndpoint();
                    WriteDiag("HTTP " + (int)response.StatusCode + " " + path + "：token 失效，端点作废");
                }

                return null;
            }

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            return JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            // 连接被拒/超时 = 端点失效（客户端关了或换了端口），作废后下次重新发现
            InvalidateEndpoint();
            WriteDiag("请求失败 " + path + "：" + ex.GetType().Name + " " + ex.Message + " → 端点作废");
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

        var gamesArray = FindGamesArray(doc.RootElement);
        if (gamesArray == null || gamesArray.Value.GetArrayLength() == 0)
        {
            return 0;
        }

        var game = gamesArray.Value[0];
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

    // ===== 英雄目录（LCU → ddragon 官方 CDN → 磁盘缓存）=====

    /// <summary>拿全部英雄（ddragon 官方中文优先——同时有本名和称号；否则 LCU；否则磁盘缓存）。</summary>
    public async Task<List<ChampionCatalogEntry>> GetChampionCatalogAsync()
    {
        if (_catalog is { Count: > 0 })
        {
            return _catalog;
        }

        var (cached, cachedVersion) = TryLoadCatalogFromDisk();
        _ddVersion ??= cachedVersion;

        var fromDd = await TryLoadCatalogFromDdragonAsync().ConfigureAwait(false);
        if (fromDd is { Count: > 0 })
        {
            _catalog = fromDd;
            SaveCatalogToDisk("ddragon");
            return _catalog;
        }

        var fromLcu = await TryLoadCatalogFromLcuAsync().ConfigureAwait(false);
        if (fromLcu is { Count: > 0 })
        {
            _catalog = fromLcu;
            SaveCatalogToDisk("lcu");
            return _catalog;
        }

        _catalog = cached ?? new List<ChampionCatalogEntry>();
        return _catalog;
    }

    private async Task<List<ChampionCatalogEntry>?> TryLoadCatalogFromLcuAsync()
    {
        var endpoint = GetEndpoint();
        if (endpoint == null)
        {
            return null;
        }

        using var doc = await GetJsonAsync(endpoint.Value, "/lol-game-data/assets/v1/champion-summary.json")
            .ConfigureAwait(false);
        if (doc == null || doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var list = new List<ChampionCatalogEntry>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var id = TryInt(item, "id");
            var name = TryString(item, "name");
            var alias = TryString(item, "alias");
            if (id > 0 && !string.IsNullOrWhiteSpace(name))
            {
                // LCU 的 name 随客户端语言，中文客户端即本名；称号字段拿不到，先用本名顶
                list.Add(new ChampionCatalogEntry(id, name!, alias, name!));
            }
        }

        return list.Count > 0 ? list : null;
    }

    private async Task<List<ChampionCatalogEntry>?> TryLoadCatalogFromDdragonAsync()
    {
        try
        {
            var versionBytes = await CdnGetBytesAsync("https://ddragon.leagueoflegends.com/api/versions.json")
                .ConfigureAwait(false);
            var version = versionBytes == null
                ? null
                : JsonSerializer.Deserialize<List<string>>(versionBytes)?.FirstOrDefault();
            if (string.IsNullOrEmpty(version))
            {
                return null;
            }

            var dataBytes = await CdnGetBytesAsync(
                "https://ddragon.leagueoflegends.com/cdn/" + version + "/data/zh_CN/champion.json").ConfigureAwait(false);
            if (dataBytes == null)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(dataBytes);
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var list = new List<ChampionCatalogEntry>();
            foreach (var prop in data.EnumerateObject())
            {
                var id = TryInt(prop.Value, "key");
                // zh_CN 里 name=称号（疾风剑豪）、title=本名（亚索）
                var title = TryString(prop.Value, "name");
                var properName = TryString(prop.Value, "title");
                if (id > 0 && !string.IsNullOrWhiteSpace(title))
                {
                    list.Add(new ChampionCatalogEntry(id, title!, prop.Name,
                        string.IsNullOrWhiteSpace(properName) ? title! : properName!));
                }
            }

            if (list.Count == 0)
            {
                return null;
            }

            _ddVersion = version;
            return list;
        }
        catch
        {
            return null;
        }
    }

    private (List<ChampionCatalogEntry>?, string?) TryLoadCatalogFromDisk()
    {
        try
        {
            if (!File.Exists(CatalogFilePath))
            {
                return (null, null);
            }

            var dto = JsonSerializer.Deserialize<CatalogFileDto>(File.ReadAllText(CatalogFilePath));
            if (dto?.Champions is not { Count: > 0 })
            {
                return (null, null);
            }

            var list = new List<ChampionCatalogEntry>();
            foreach (var entry in dto.Champions)
            {
                if (entry is { Id: > 0 } && !string.IsNullOrWhiteSpace(entry.Name))
                {
                    list.Add(new ChampionCatalogEntry(entry.Id, entry.Name, entry.DdKey,
                        string.IsNullOrWhiteSpace(entry.ProperName) ? entry.Name : entry.ProperName!));
                }
            }

            return (list.Count > 0 ? list : null, dto.Version);
        }
        catch
        {
            return (null, null);
        }
    }

    private void SaveCatalogToDisk(string source)
    {
        try
        {
            if (_catalog is not { Count: > 0 })
            {
                return;
            }

            var dto = new CatalogFileDto
            {
                Version = _ddVersion,
                FetchedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
                Source = source,
                Champions = _catalog.Select(c => new CatalogEntryDto
                {
                    Id = c.Id,
                    Name = c.Name,
                    ProperName = c.ProperName,
                    DdKey = c.DdKey
                }).ToList(),
            };
            var dir = Path.GetDirectoryName(CatalogFilePath);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(CatalogFilePath, JsonSerializer.Serialize(dto));
        }
        catch
        {
            // 缓存写失败无所谓
        }
    }

    private static async Task<byte[]?> CdnGetBytesAsync(string url)
    {
        try
        {
            using var response = await CdnHttp.GetAsync(url).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>确保某英雄头像已缓存：本地 → LCU → ddragon。返回最终是否有可用头像。</summary>
    public async Task<bool> EnsureChampionIconAsync(int championId)
    {
        if (championId <= 0)
        {
            return false;
        }

        var savePath = GameSessionService.GetChampionIconPath(championId);
        if (IsIconCached(championId))
        {
            return true;
        }

        if (await TryDownloadChampionIconAsync(championId, savePath).ConfigureAwait(false))
        {
            return true;
        }

        try
        {
            var catalog = await GetChampionCatalogAsync().ConfigureAwait(false);
            var entry = catalog.FirstOrDefault(c => c.Id == championId);
            if (entry == null || string.IsNullOrEmpty(entry.DdKey))
            {
                return false;
            }

            if (_ddVersion == null)
            {
                var versionBytes = await CdnGetBytesAsync("https://ddragon.leagueoflegends.com/api/versions.json")
                    .ConfigureAwait(false);
                _ddVersion = versionBytes == null
                    ? null
                    : JsonSerializer.Deserialize<List<string>>(versionBytes)?.FirstOrDefault();
                if (_ddVersion == null)
                {
                    return false;
                }
            }

            var bytes = await CdnGetBytesAsync(
                "https://ddragon.leagueoflegends.com/cdn/" + _ddVersion + "/img/champion/" + entry.DdKey + ".png")
                .ConfigureAwait(false);
            if (bytes == null || bytes.Length < 100)
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
            return false;
        }
    }

    /// <summary>批量补齐缺失头像（逐个限速）。返回本次新下载的个数。</summary>
    public async Task<int> EnsureChampionIconsAsync(
        IReadOnlyCollection<int> championIds, Action<int>? onEachDone = null)
    {
        var downloaded = 0;
        foreach (var id in championIds)
        {
            var existed = IsIconCached(id);
            if (await EnsureChampionIconAsync(id).ConfigureAwait(false))
            {
                if (!existed)
                {
                    downloaded++;
                    onEachDone?.Invoke(id);
                }
            }

            await Task.Delay(80).ConfigureAwait(false);
        }

        return downloaded;
    }

    public static bool IsIconCached(int championId)
    {
        try
        {
            var path = GameSessionService.GetChampionIconPath(championId);
            return File.Exists(path) && new FileInfo(path).Length > 100;
        }
        catch
        {
            return false;
        }
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

/// <summary>英雄目录条目（Name=称号如「疾风剑豪」，ProperName=本名如「亚索」，DdKey=ddragon 英雄代码）。</summary>
internal sealed class ChampionCatalogEntry
{
    public ChampionCatalogEntry(int id, string name, string? ddKey, string properName)
    {
        Id = id;
        Name = name;
        DdKey = ddKey;
        ProperName = properName;
    }

    public int Id { get; }
    public string Name { get; }
    public string? DdKey { get; }

    /// <summary>本名（亚索/亚托克斯）；老缓存或拿不到时回退到称号。</summary>
    public string ProperName { get; }

    /// <summary>展示与入库用的英雄名：优先本名。</summary>
    public string DisplayName =>
        string.IsNullOrWhiteSpace(ProperName) ? Name : ProperName;
}

internal sealed class CatalogFileDto
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("fetchedAt")]
    public string? FetchedAt { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("champions")]
    public List<CatalogEntryDto> Champions { get; set; } = new();
}

internal sealed class CatalogEntryDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("properName")]
    public string? ProperName { get; set; }

    [JsonPropertyName("ddKey")]
    public string? DdKey { get; set; }
}
