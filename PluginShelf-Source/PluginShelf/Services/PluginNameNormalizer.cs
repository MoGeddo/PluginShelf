using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PluginShelf.Services;

public sealed record NormalizedPluginName(string NameKey, string CoreNameKey, string VendorKey);

public static class PluginNameNormalizer
{
    private static readonly Regex Separators = new("[^\\p{L}\\p{N}]+", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex BitnessQualifiers = new("\\b(?:32|64)\\s*[-_ ]?\\s*bits?\\b|\\b(?:x86|x64|i386|i686|win32|win64)\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> FormatTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "vst", "vst1", "vst2", "vst3", "clap", "plugin", "plugins", "x86", "x64", "i386", "i686", "win32", "win64", "32bit", "64bit"
    };

    public static NormalizedPluginName Normalize(string? name, string? vendor)
    {
        var nameWords = GetWords(name);
        var vendorWords = GetWords(vendor);
        var nameKey = string.Concat(nameWords);
        var vendorKey = string.Concat(vendorWords);

        // Strip a company prefix only for alias suggestions. The full NameKey is retained,
        // so a vendor-prefixed alias is never silently equated with an exact product name.
        var coreWords = new List<string>(nameWords);
        if (vendorWords.Count > 0 && coreWords.Count >= vendorWords.Count)
        {
            var prefixMatches = true;
            for (var i = 0; i < vendorWords.Count; i++)
                if (!string.Equals(coreWords[i], vendorWords[i], StringComparison.OrdinalIgnoreCase))
                    prefixMatches = false;
            if (prefixMatches) coreWords.RemoveRange(0, vendorWords.Count);
        }

        return new NormalizedPluginName(nameKey, string.Concat(coreWords), vendorKey);
    }

    public static string NormalizeVendor(string? vendor) => string.Concat(GetWords(vendor));

    private static List<string> GetWords(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new List<string>();
        var decomposed = value.Normalize(NormalizationForm.FormKD);
        var prepared = BitnessQualifiers.Replace(decomposed, " ");
        var clean = new StringBuilder(prepared.Length);
        foreach (var c in prepared)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                clean.Append(char.ToLowerInvariant(c));
        }

        var words = Separators.Split(clean.ToString())
            .Where(w => w.Length > 0 && !FormatTokens.Contains(w))
            .Select(w => w.ToLowerInvariant())
            .ToList();

        // Ignore common legal suffixes, but preserve meaningful model digits and tokens.
        while (words.Count > 0 && words[^1] is "inc" or "llc" or "ltd" or "limited" or "gmbh" or "audio")
            words.RemoveAt(words.Count - 1);
        return words;
    }
}
