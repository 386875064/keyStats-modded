using System;
using System.Collections.Generic;

namespace KeyStats.Services;

/// <summary>
/// Declarative description of a game whose sessions should be tracked separately.
/// Add a new entry to <see cref="GameProfileCatalog.All"/> to support another game.
/// </summary>
public sealed class GameProfile
{
    public GameProfile(
        string id,
        string fallbackName,
        string[] sessionProcesses,
        string[]? companionProcesses = null)
    {
        Id = id;
        FallbackName = fallbackName;
        SessionProcesses = sessionProcesses;
        CompanionProcesses = companionProcesses ?? Array.Empty<string>();
    }

    /// <summary>Stable identifier used for persistence and localization lookups.</summary>
    public string Id { get; }

    /// <summary>Name used when no localized resource is available.</summary>
    public string FallbackName { get; }

    /// <summary>
    /// Processes whose presence starts a tracked session (i.e. the actual match client).
    /// </summary>
    public string[] SessionProcesses { get; }

    /// <summary>
    /// Auxiliary processes launched alongside the game (launchers, lobby clients).
    /// They never start a session; they only mark the game as "running".
    /// </summary>
    public string[] CompanionProcesses { get; }

    public bool MatchesSessionProcess(string? processName)
    {
        return MatchesAny(SessionProcesses, processName);
    }

    public bool MatchesAnyProcess(string? processName)
    {
        return MatchesAny(SessionProcesses, processName) || MatchesAny(CompanionProcesses, processName);
    }

    private static bool MatchesAny(string[] candidates, string? processName)
    {
        var normalized = NormalizeProcessName(processName);
        if (normalized.Length == 0)
        {
            return false;
        }

        foreach (var candidate in candidates)
        {
            var normalizedCandidate = NormalizeProcessName(candidate);
            if (normalizedCandidate.Length == 0)
            {
                continue;
            }

            if (string.Equals(normalized, normalizedCandidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal static string NormalizeProcessName(string? processName)
    {
        var trimmed = (processName ?? string.Empty).Trim();
        if (trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 4);
        }

        return trimmed;
    }
}

public static class GameProfileCatalog
{
    private static readonly Dictionary<string, GameProfile> SessionProcessLookup =
        new(StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, GameProfile> AnyProcessLookup =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// League of Legends. Covers both the international client and the mainland
    /// China client (WeGame / TCLS / Riot Client shells).
    /// </summary>
    public static GameProfile LeagueOfLegends { get; } = new(
        id: "lol",
        fallbackName: "League of Legends",
        sessionProcesses: new[]
        {
            "League of Legends",
            "LeagueofLegends"
        },
        companionProcesses: new[]
        {
            "LeagueClient",
            "LeagueClientUx",
            "LeagueClientUxRender",
            "RiotClientServices",
            "RiotClientUx",
            "RiotClientUxRender",
            "TCLS",
            "WeGame",
            "wegame",
            "TenioDL"
        });

    public static IReadOnlyList<GameProfile> All { get; } = new[] { LeagueOfLegends };

    public static GameProfile? FindById(string? gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return null;
        }

        var normalizedId = gameId!.Trim();
        foreach (var profile in All)
        {
            if (string.Equals(profile.Id, normalizedId, StringComparison.OrdinalIgnoreCase))
            {
                return profile;
            }
        }

        return null;
    }

    /// <summary>Returns the first profile whose session process matches.</summary>
    public static GameProfile? FindBySessionProcess(string? processName)
    {
        return Lookup(SessionProcessLookup, processName);
    }

    /// <summary>Returns the first profile that owns either a session or a companion process.</summary>
    public static GameProfile? FindByAnyProcess(string? processName)
    {
        return Lookup(AnyProcessLookup, processName);
    }

    private static GameProfile? Lookup(Dictionary<string, GameProfile> lookup, string? processName)
    {
        var key = GameProfile.NormalizeProcessName(processName);
        return key.Length > 0 && lookup.TryGetValue(key, out var profile) ? profile : null;
    }

    static GameProfileCatalog()
    {
        foreach (var profile in All)
        {
            foreach (var process in profile.SessionProcesses)
            {
                var key = GameProfile.NormalizeProcessName(process);
                if (key.Length > 0)
                {
                    SessionProcessLookup[key] = profile;
                    AnyProcessLookup[key] = profile;
                }
            }

            foreach (var process in profile.CompanionProcesses)
            {
                var key = GameProfile.NormalizeProcessName(process);
                if (key.Length > 0 && !SessionProcessLookup.ContainsKey(key))
                {
                    AnyProcessLookup[key] = profile;
                }
            }
        }
    }

    /// <summary>Localized game name, falling back to the profile's English label.</summary>
    public static string GetDisplayName(GameProfile profile)
    {
        string? localized = profile.Id switch
        {
            "lol" => KeyStats.Properties.Strings.Game_LoL_Name,
            _ => null
        };

        return string.IsNullOrWhiteSpace(localized) ? profile.FallbackName : localized!;
    }
}
