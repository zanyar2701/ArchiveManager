using System.Text;
using System.Text.RegularExpressions;

namespace ArchiveManager.Infrastructure.FileSystem;

/// <summary>
/// Normalizes Persian/Arabic text so that character variants match consistently
/// in filenames, sorting, and search (spec §13). Also strips characters that
/// are invalid in a Windows filename.
/// </summary>
public sealed class PersianTextNormalizer
{
    // Arabic ي/ك → Persian ی/ک; Arabic-Indic/Extended digits → Persian digits
    // are intentionally NOT force-converted here (spec §14: digit style is a
    // user Setting, not a silent transform) — only character-shape normalization
    // happens unconditionally, since that affects correctness, not preference.
    private static readonly (char From, char To)[] CharacterMap =
    {
        ('ي', 'ی'),
        ('ك', 'ک'),
        ('\u200C', ' '), // zero-width non-joiner → space, simplifies filename joins
    };

    private static readonly char[] WindowsInvalidChars = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
    private static readonly Regex MultiSpace = new(@"\s{2,}", RegexOptions.Compiled);

    public string NormalizeDisplayText(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (var ch in input)
        {
            var mapped = ch;
            foreach (var (from, to) in CharacterMap)
            {
                if (ch == from) { mapped = to; break; }
            }
            sb.Append(mapped);
        }

        var collapsed = MultiSpace.Replace(sb.ToString(), " ").Trim();
        return collapsed;
    }

    /// <summary>
    /// Strips Windows-invalid filename characters (spec §13/§7 step 6) and
    /// re-collapses whitespace left behind, then normalizes character shapes.
    /// Does NOT touch the file extension — pass the base name only.
    /// </summary>
    public string SanitizeForFilename(string input)
    {
        var normalized = NormalizeDisplayText(input);
        var sb = new StringBuilder(normalized.Length);
        foreach (var ch in normalized)
        {
            sb.Append(System.Array.IndexOf(WindowsInvalidChars, ch) >= 0 ? ' ' : ch);
        }
        return MultiSpace.Replace(sb.ToString(), " ").Trim();
    }
}
