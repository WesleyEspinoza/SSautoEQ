using SteelSeriesAutoEq.Models;

namespace SteelSeriesAutoEq.Services;

/// <summary>
/// Scores how well a running app matches each Sonar profile and returns the best candidate.
/// Used to suggest a profile when the user is assigning one; it does not switch anything itself.
/// </summary>
public sealed class ProfileMatcher
{
    private readonly double _fuzzyThreshold;

    public ProfileMatcher(double fuzzyThreshold = 0.72)
    {
        _fuzzyThreshold = fuzzyThreshold;
    }

    public MatchResult? FindBestMatch(
        ForegroundAppInfo app,
        IReadOnlyList<SonarProfile> profiles)
    {
        if (profiles.Count == 0)
        {
            return null;
        }

        // Manual processMatches on any profile still win.
        MatchResult? bestManual = null;
        foreach (var profile in profiles)
        {
            var manual = EvaluateManualOverride(app, profile);
            if (manual is null)
            {
                continue;
            }

            if (bestManual is null || manual.Score > bestManual.Score)
            {
                bestManual = manual;
            }
        }

        if (bestManual is not null)
        {
            return bestManual;
        }

        // Curated game aliases (cs2.exe → "CS2 Pro Preset", etc.)
        var alias = GameAliasCatalog.FindAlias(app);
        if (alias is not null)
        {
            var aliased = GameAliasCatalog.FindBestProfile(alias, profiles);
            if (aliased is not null)
            {
                return new MatchResult
                {
                    Profile = aliased,
                    Priority = MatchPriority.GameAlias,
                    Reason = $"game alias ({alias.DisplayName} → {aliased.Name})",
                    Score = 1.0
                };
            }
        }

        MatchResult? best = null;
        foreach (var profile in profiles)
        {
            var candidate = Evaluate(app, profile);
            if (candidate is null)
            {
                continue;
            }

            if (best is null ||
                candidate.Priority > best.Priority ||
                (candidate.Priority == best.Priority && candidate.Score > best.Score))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static MatchResult? EvaluateManualOverride(ForegroundAppInfo app, SonarProfile profile)
    {
        var processExe = app.ExecutableName;
        var processBase = TextNormalizer.Normalize(TextNormalizer.StripExtension(processExe));
        var processFull = TextNormalizer.Normalize(processExe);

        foreach (var overrideMatch in profile.ProcessMatches)
        {
            var overrideNorm = TextNormalizer.Normalize(overrideMatch);
            var overrideBase = TextNormalizer.Normalize(TextNormalizer.StripExtension(overrideMatch));
            if (string.IsNullOrEmpty(overrideNorm))
            {
                continue;
            }

            if (overrideNorm == processFull ||
                overrideNorm == processBase ||
                overrideBase == processBase)
            {
                return new MatchResult
                {
                    Profile = profile,
                    Priority = MatchPriority.ExactProcessName,
                    Reason = $"manual process match ({overrideMatch})",
                    Score = 1.0
                };
            }
        }

        return null;
    }

    private MatchResult? Evaluate(ForegroundAppInfo app, SonarProfile profile)
    {
        var processExe = app.ExecutableName;
        var processBase = TextNormalizer.Normalize(TextNormalizer.StripExtension(processExe));
        var windowNorm = TextNormalizer.Normalize(app.WindowTitle);
        var profileNorm = profile.NormalizedName;

        // 1) Exact process name match
        if (!string.IsNullOrEmpty(processBase) && profileNorm == processBase)
        {
            return new MatchResult
            {
                Profile = profile,
                Priority = MatchPriority.ExactProcessName,
                Reason = $"exact process name ({processExe})",
                Score = 1.0
            };
        }

        // Short process names (cs2) contained in profile name
        if (processBase.Length is >= 2 and <= 5 &&
            profileNorm.Contains(processBase, StringComparison.Ordinal))
        {
            return new MatchResult
            {
                Profile = profile,
                Priority = MatchPriority.PartialProfileName,
                Reason = $"short process in profile ({processExe} ∈ {profile.Name})",
                Score = 0.9 + (processBase.Length / 50.0)
            };
        }

        // 2) Exact window title match
        if (!string.IsNullOrEmpty(windowNorm) && windowNorm == profileNorm)
        {
            return new MatchResult
            {
                Profile = profile,
                Priority = MatchPriority.ExactWindowTitle,
                Reason = $"exact window title ({app.WindowTitle})",
                Score = 1.0
            };
        }

        // 3) Partial profile name match
        if (!string.IsNullOrEmpty(profileNorm))
        {
            if (!string.IsNullOrEmpty(processBase) &&
                TokensOverlap(profileNorm, processBase))
            {
                return new MatchResult
                {
                    Profile = profile,
                    Priority = MatchPriority.PartialProfileName,
                    Reason = $"partial process/profile ({processExe} ~ {profile.Name})",
                    Score = Similarity(profileNorm, processBase)
                };
            }

            if (!string.IsNullOrEmpty(windowNorm) &&
                TokensOverlap(profileNorm, windowNorm))
            {
                return new MatchResult
                {
                    Profile = profile,
                    Priority = MatchPriority.PartialProfileName,
                    Reason = $"partial window/profile ({app.WindowTitle} ~ {profile.Name})",
                    Score = Similarity(profileNorm, windowNorm)
                };
            }
        }

        // 4) Fuzzy string similarity
        var bestFuzzy = 0.0;
        string? fuzzyAgainst = null;

        if (!string.IsNullOrEmpty(processBase))
        {
            var score = Similarity(profileNorm, processBase);
            if (score > bestFuzzy)
            {
                bestFuzzy = score;
                fuzzyAgainst = processExe;
            }
        }

        if (!string.IsNullOrEmpty(windowNorm))
        {
            var score = Similarity(profileNorm, windowNorm);
            if (score > bestFuzzy)
            {
                bestFuzzy = score;
                fuzzyAgainst = app.WindowTitle;
            }
        }

        if (bestFuzzy >= _fuzzyThreshold && fuzzyAgainst is not null)
        {
            return new MatchResult
            {
                Profile = profile,
                Priority = MatchPriority.Fuzzy,
                Reason = $"fuzzy match ({fuzzyAgainst} ~ {profile.Name}, {bestFuzzy:F2})",
                Score = bestFuzzy
            };
        }

        return null;
    }

    private static bool TokensOverlap(string a, string b)
    {
        // Allow short tokens like "cs2" (3 chars) when matching profiles.
        if (a.Length < 3 || b.Length < 3)
        {
            return false;
        }

        return a.Contains(b, StringComparison.Ordinal) ||
               b.Contains(a, StringComparison.Ordinal);
    }

    public static double Similarity(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return 0;
        }

        if (a == b)
        {
            return 1;
        }

        if (a.Length == 1 || b.Length == 1)
        {
            return a[0] == b[0] ? 1 : 0;
        }

        var bigramsA = GetBigrams(a);
        var bigramsB = GetBigrams(b);
        var overlap = 0;

        foreach (var (gram, countA) in bigramsA)
        {
            if (bigramsB.TryGetValue(gram, out var countB))
            {
                overlap += Math.Min(countA, countB);
            }
        }

        return (2.0 * overlap) / (bigramsA.Values.Sum() + bigramsB.Values.Sum());
    }

    private static Dictionary<string, int> GetBigrams(string value)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < value.Length - 1; i++)
        {
            var gram = value.Substring(i, 2);
            map[gram] = map.TryGetValue(gram, out var count) ? count + 1 : 1;
        }

        return map;
    }
}
