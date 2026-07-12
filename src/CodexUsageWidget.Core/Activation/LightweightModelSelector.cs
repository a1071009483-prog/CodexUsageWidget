using System.Collections.ObjectModel;
using CodexUsageWidget.Core.Abstractions;

namespace CodexUsageWidget.Core.Activation;

/// <summary>
/// Curated lightweight-model selection policy.
/// </summary>
public static class LightweightModelSelector
{
    // Highest-priority families first.
    private static readonly ReadOnlyCollection<string> RecognizedFamilies =
        new(["gpt-4o-mini", "o4-mini", "o3-mini", "gpt-3.5-turbo"]);

    private static readonly Comparer<string?> VersionComparer =
        Comparer<string?>.Create(CompareVersions);

    /// <summary>
    /// Selects the best lightweight model from the catalog.
    /// Returns null when no acceptable candidate is available.
    /// </summary>
    public static ModelSelectionResult? Select(IReadOnlyList<ModelCandidate> catalog)
    {
        if (catalog is null || catalog.Count == 0)
        {
            return null;
        }

        foreach (string family in RecognizedFamilies)
        {
            List<ModelCandidate> matches = new(capacity: catalog.Count);
            foreach (ModelCandidate candidate in catalog)
            {
                if (MatchesFamily(candidate, family))
                {
                    matches.Add(candidate);
                }
            }

            if (matches.Count > 0)
            {
                ModelCandidate selected = matches
                    .OrderByDescending(static c => ExtractVersion(c.Id), VersionComparer)
                    .First();
                return new ModelSelectionResult(selected, UsedFallback: false);
            }
        }

        ModelCandidate? defaultCandidate = null;
        foreach (ModelCandidate candidate in catalog)
        {
            if (candidate.IsDefault)
            {
                if (defaultCandidate is not null)
                {
                    return null;
                }

                defaultCandidate = candidate;
            }
        }

        return defaultCandidate is null ? null : new ModelSelectionResult(defaultCandidate, UsedFallback: true);
    }

    private static bool MatchesFamily(ModelCandidate candidate, string family) =>
        candidate.Id.Contains(family, StringComparison.OrdinalIgnoreCase)
        || candidate.Model.Contains(family, StringComparison.OrdinalIgnoreCase);

    private static string? ExtractVersion(string id)
    {
        string[] segments = id.Split('-');
        if (segments.Length == 0)
        {
            return null;
        }

        int end = segments.Length - 1;
        while (end >= 0)
        {
            string segment = segments[end];
            if (segment.Length == 0)
            {
                end--;
                continue;
            }

            string normalized = segment;
            if (normalized[0] == 'v' || normalized[0] == 'V')
            {
                normalized = normalized.Substring(1);
            }

            if (normalized.Length > 0 && char.IsDigit(normalized[0]))
            {
                end--;
                continue;
            }

            break;
        }

        int start = end + 1;
        if (start >= segments.Length)
        {
            return null;
        }

        string version = string.Join('-', segments, start, segments.Length - start);
        if (version.Length > 0 && (version[0] == 'v' || version[0] == 'V'))
        {
            version = version.Substring(1);
        }

        if (version.Length > 0 && char.IsDigit(version[0]))
        {
            return version;
        }

        return null;
    }

    private static int CompareVersions(string? a, string? b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        if (a is null)
        {
            return b is null ? 0 : -1;
        }

        if (b is null)
        {
            return 1;
        }

        if (Version.TryParse(a, out Version? versionA) && Version.TryParse(b, out Version? versionB))
        {
            return versionA.CompareTo(versionB);
        }

        // ISO date strings and other dotted identifiers compare lexicographically.
        return string.CompareOrdinal(a, b);
    }
}
