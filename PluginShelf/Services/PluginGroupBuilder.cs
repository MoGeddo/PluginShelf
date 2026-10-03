using System.Text.RegularExpressions;
using PluginShelf.Models;

namespace PluginShelf.Services;

public static class PluginGroupBuilder
{
    private static readonly Regex VersionPattern = new(@"(?<!\d)(\d+(?:\.\d+){1,3})(?!\d)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static List<PluginGroup> Build(IReadOnlyCollection<PluginCandidate> candidates)
    {
        var all = candidates
            .Where(c => c.IsLikelyPlugin && !c.IsMultiComponent &&
                        !PluginSafety.IsProtectedCandidate(c.Path, c.Vendor, c.Name) &&
                        !string.IsNullOrWhiteSpace(c.CoreNameKey))
            .ToList();

        var groups = new List<PluginGroup>();
        foreach (var bucket in all.GroupBy(c => c.CoreNameKey, StringComparer.OrdinalIgnoreCase))
        {
            var members = bucket.ToList();
            if (!HasMeaningfulVariants(members)) continue;

            var nameKeys = members.Select(c => c.NameKey)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var vendorKeys = members.Select(c => c.VendorKey)
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            // An identical normalized product/generation/I/O name is decisive on its own.
            // Missing vendor metadata is common (many plug-ins embed none) and must not
            // downgrade confidence, but two *different* vendor identities remain a
            // possible alias and still require a human.
            var conflictingVendors = vendorKeys.Count > 1;
            var highConfidence = nameKeys == 1 && !conflictingVendors;

            // Channel/side-chain and product-generation markers are kept in CoreNameKey.
            // A vendor prefix can still be suggested as an alias, but never silently merged.
            var group = CreateGroup(members,
                needsIdentityConfirmation: !highConfidence,
                isAlias: !highConfidence,
                summary: highConfidence
                    ? "Exact product, generation, and I/O variant match"
                    : "Possible name/vendor alias; confirm these are the same product generation and I/O variant");
            groups.Add(group);
        }

        return groups
            .OrderByDescending(g => g.Candidates.Count)
            .ThenBy(g => g.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static bool HasMeaningfulVariants(IReadOnlyCollection<PluginCandidate> members) =>
        members.Select(c => Path.GetFullPath(c.Path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Count() > 1;

    private static PluginGroup CreateGroup(List<PluginCandidate> members, bool needsIdentityConfirmation,
        bool isAlias, string summary)
    {
        var sorted = members.OrderBy(c => c.ArchitectureRank)
            .ThenBy(c => c.FormatRank)
            .ThenByDescending(c => ParseProductVersion(c.Version), NullableVersionComparer.Instance)
            .ThenBy(c => c.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var top = FindTopCandidates(sorted, out var versionAmbiguous);
        var hasTie = top.Count > 1;
        var canRecommend = !hasTie && top[0].Architecture is PluginArchitecture.X64 or PluginArchitecture.X86;
        var recommended = canRecommend ? top[0] : null;
        var requiresManualChoice = !canRecommend;
        var displayName = members
            .OrderByDescending(c => c.Name.Length)
            .Select(c => c.Name)
            .FirstOrDefault() ?? "Possible plug-in group";
        var vendor = members.Select(c => c.Vendor)
            .FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

        var tieDetail = versionAmbiguous
            ? "some release versions could not be compared; choose manually"
            : "more than one candidate has the same priority—choose manually";
        return new PluginGroup
        {
            DisplayName = displayName,
            Vendor = vendor,
            MatchSummary = requiresManualChoice ? $"{summary}; {tieDetail}" : summary,
            NeedsIdentityConfirmation = needsIdentityConfirmation,
            IsIdentityConfirmed = false,
            IncludeInPlan = false,
            IsSuggestedAlias = isAlias,
            HasConflictingTopCandidates = requiresManualChoice,
            Candidates = new System.Collections.ObjectModel.ObservableCollection<PluginCandidate>(sorted),
            RecommendedKeepId = recommended?.Id,
            SelectedKeepId = needsIdentityConfirmation ? null : recommended?.Id
        };
    }

    private static List<PluginCandidate> FindTopCandidates(List<PluginCandidate> sorted, out bool versionAmbiguous)
    {
        var bestArchitecture = sorted.Min(c => c.ArchitectureRank);
        var architectureCandidates = sorted.Where(c => c.ArchitectureRank == bestArchitecture).ToList();
        var bestFormat = architectureCandidates.Min(c => c.FormatRank);
        var preferredFormatCandidates = architectureCandidates
            .Where(c => c.FormatRank == bestFormat)
            .ToList();
        var parsed = preferredFormatCandidates
            .Select(c => (Candidate: c, Version: ParseProductVersion(c.Version)))
            .ToList();
        var hasAnyVersion = parsed.Any(x => x.Version is not null);
        var hasAllVersions = parsed.All(x => x.Version is not null);
        versionAmbiguous = hasAnyVersion && !hasAllVersions;

        // Format preference is decisive before release version. Within that winning tier,
        // mixed version metadata cannot safely distinguish candidates, so require a human.
        if (versionAmbiguous) return preferredFormatCandidates;

        if (hasAllVersions)
        {
            var newest = parsed.Max(x => x.Version)!;
            return parsed.Where(x => x.Version!.Equals(newest)).Select(x => x.Candidate).ToList();
        }

        // No comparable release metadata among the preferred-format candidates.
        // A sole candidate is still a safe keeper; multiple candidates remain tied for review.
        return preferredFormatCandidates;
    }

    private static Version? ParseProductVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var match = VersionPattern.Match(text);
        if (!match.Success) return null;
        var parts = match.Groups[1].Value.Split('.').Select(p => int.TryParse(p, out var n) ? n : -1).ToArray();
        if (parts.Length < 2 || parts.Any(p => p < 0)) return null;
        var padded = parts.Concat(Enumerable.Repeat(0, 4 - parts.Length)).ToArray();
        return padded.Length == 4 ? new Version(padded[0], padded[1], padded[2], padded[3]) : null;
    }

    private sealed class NullableVersionComparer : IComparer<Version?>
    {
        public static readonly NullableVersionComparer Instance = new();
        public int Compare(Version? x, Version? y) =>
            x is null ? y is null ? 0 : -1 : y is null ? 1 : x.CompareTo(y);
    }
}
