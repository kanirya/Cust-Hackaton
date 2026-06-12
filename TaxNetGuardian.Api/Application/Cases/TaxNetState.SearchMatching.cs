using System.Globalization;
using System.Text;

namespace TaxNetGuardian.Api;

// Name-matching engine for the unified search. Handles three hard problems for Pakistani data:
//   1. Spelling variants  — Muhammad/Mohammad/Mohd, Ahmed/Ahmad, Siddiqui/Siddiqi, Chaudhry/Choudhary...
//   2. Urdu script        — diacritic/alef/yeh normalization + romanization so Urdu queries match.
//   3. Cross-script       — an Urdu query can resolve a Latin-named record and vice-versa, because
//                            both sides are reduced to the same romanized + phonetic skeleton.
// Everything is deterministic and self-contained (no external services).
public static class NameMatching
{
    // ---- Pakistani name spelling-variant canonicalization (token -> canonical form) ----
    private static readonly IReadOnlyDictionary<string, string> VariantCanon = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["muhammad"] = "muhammad", ["mohammad"] = "muhammad", ["mohammed"] = "muhammad", ["mohamed"] = "muhammad",
        ["mohamad"] = "muhammad", ["muhammed"] = "muhammad", ["mohd"] = "muhammad", ["muhd"] = "muhammad",
        ["md"] = "muhammad", ["mhd"] = "muhammad", ["mhmd"] = "muhammad",
        ["ahmad"] = "ahmad", ["ahmed"] = "ahmad", ["ahmd"] = "ahmad",
        ["ali"] = "ali", ["aly"] = "ali",
        ["hussain"] = "hussain", ["hussein"] = "hussain", ["husain"] = "hussain", ["hossain"] = "hussain",
        ["siddiqui"] = "siddiqui", ["siddiqi"] = "siddiqui", ["sidiqui"] = "siddiqui", ["siddqui"] = "siddiqui", ["siddique"] = "siddiqui",
        ["qureshi"] = "qureshi", ["quraishi"] = "qureshi", ["qurashi"] = "qureshi", ["quraishe"] = "qureshi",
        ["chaudhry"] = "chaudhry", ["chaudhary"] = "chaudhry", ["choudhry"] = "chaudhry", ["choudhary"] = "chaudhry",
        ["chauhdry"] = "chaudhry", ["chowdhry"] = "chaudhry", ["ch"] = "chaudhry",
        ["sheikh"] = "sheikh", ["shaikh"] = "sheikh", ["shaykh"] = "sheikh", ["sheik"] = "sheikh",
        ["syed"] = "syed", ["sayed"] = "syed", ["sayyed"] = "syed", ["saiyed"] = "syed",
        ["abbasi"] = "abbasi", ["abasi"] = "abbasi",
        ["khan"] = "khan", ["kahn"] = "khan",
        ["malik"] = "malik", ["malick"] = "malik", ["maalik"] = "malik",
        ["butt"] = "butt", ["bhutt"] = "butt",
        ["rana"] = "rana",
        ["javed"] = "javed", ["javaid"] = "javed", ["javid"] = "javed",
        ["akhtar"] = "akhtar", ["akhter"] = "akhtar",
        ["mirza"] = "mirza",
        ["gill"] = "gill", ["gil"] = "gill",
        ["dar"] = "dar",
        ["fatima"] = "fatima", ["fatma"] = "fatima",
        ["ayesha"] = "ayesha", ["aisha"] = "ayesha", ["aysha"] = "ayesha",
        ["usman"] = "usman", ["uthman"] = "usman",
        ["bilal"] = "bilal",
        ["hamza"] = "hamza", ["hamzah"] = "hamza",
        ["zain"] = "zain", ["zayn"] = "zain",
        ["noor"] = "noor", ["nur"] = "noor",
        ["mariam"] = "mariam", ["maryam"] = "mariam", ["marium"] = "mariam"
    };

    // Urdu/Arabic letter -> Latin romanization (consonant-leaning; vowels approximate).
    private static readonly IReadOnlyDictionary<char, string> UrduToLatin = new Dictionary<char, string>
    {
        ['ا'] = "a", ['آ'] = "a", ['أ'] = "a", ['إ'] = "a", ['ء'] = "", ['ئ'] = "y", ['ؤ'] = "o",
        ['ب'] = "b", ['پ'] = "p", ['ت'] = "t", ['ٹ'] = "t", ['ث'] = "s",
        ['ج'] = "j", ['چ'] = "ch", ['ح'] = "h", ['خ'] = "kh",
        ['د'] = "d", ['ڈ'] = "d", ['ذ'] = "z", ['ر'] = "r", ['ڑ'] = "r", ['ز'] = "z", ['ژ'] = "zh",
        ['س'] = "s", ['ش'] = "sh", ['ص'] = "s", ['ض'] = "z", ['ط'] = "t", ['ظ'] = "z",
        ['ع'] = "a", ['غ'] = "gh", ['ف'] = "f", ['ق'] = "q", ['ک'] = "k", ['ك'] = "k", ['گ'] = "g",
        ['ل'] = "l", ['م'] = "m", ['ن'] = "n", ['ں'] = "n", ['ھ'] = "h", ['ہ'] = "h", ['ة'] = "h",
        ['و'] = "o", ['ی'] = "y", ['ي'] = "y", ['ے'] = "e"
    };

    public static bool ContainsUrduScript(string? text)
        => !string.IsNullOrEmpty(text) && text.Any(c => c >= '\u0600' && c <= '\u06FF');

    // Normalize Urdu text: drop diacritics/harakat, tatweel, ZWNJ; unify alef/yeh/heh/kaf variants.
    public static string NormalizeUrdu(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            // Skip Arabic diacritics (harakat), superscript alef, tatweel, ZWNJ/ZWJ.
            if ((ch >= '\u064B' && ch <= '\u0652') || ch == '\u0670' || ch == '\u0640' || ch == '\u200C' || ch == '\u200D')
            {
                continue;
            }

            var mapped = ch switch
            {
                'آ' or 'أ' or 'إ' or 'ٱ' => 'ا',
                'ي' or 'ئ' => 'ی',
                'ك' => 'ک',
                'ة' => 'ہ',
                'ھ' => 'ہ',
                _ => ch
            };
            sb.Append(mapped);
        }

        return CollapseSpaces(sb.ToString());
    }

    public static string RomanizeUrdu(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var normalized = NormalizeUrdu(text);
        var sb = new StringBuilder(normalized.Length * 2);
        foreach (var ch in normalized)
        {
            if (ch == ' ')
            {
                sb.Append(' ');
            }
            else if (UrduToLatin.TryGetValue(ch, out var latin))
            {
                sb.Append(latin);
            }
            // Unknown glyphs are dropped.
        }

        return CollapseSpaces(sb.ToString().ToLowerInvariant());
    }

    // Lowercase, strip accents/punctuation, collapse whitespace.
    public static string NormalizeLatin(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return "";
        }

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(char.ToLowerInvariant(ch));
            }
            else if (char.IsWhiteSpace(ch) || ch is '-' or '.' or '\'' or ',')
            {
                sb.Append(' ');
            }
        }

        return CollapseSpaces(sb.ToString());
    }

    public static string[] Tokenize(string normalized)
        => string.IsNullOrWhiteSpace(normalized)
            ? []
            : normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    // Canonicalize a token through the spelling-variant map (falls back to the token itself).
    public static string CanonToken(string token)
        => VariantCanon.TryGetValue(token, out var canon) ? canon : token;

    public static string[] CanonTokens(IEnumerable<string> tokens)
        => tokens.Select(CanonToken).Where(t => t.Length > 0).ToArray();

    // Consonant skeleton: drop vowels (keep a leading vowel), collapse doubles. Bridges spelling
    // gaps that the variant map misses and helps cross-script romanized comparison.
    public static string ConsonantSkeleton(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "";
        }

        var sb = new StringBuilder(token.Length);
        for (var i = 0; i < token.Length; i++)
        {
            var c = token[i];
            var isVowel = c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
            if (isVowel && i != 0)
            {
                continue;
            }

            if (sb.Length > 0 && sb[^1] == c)
            {
                continue; // collapse doubles
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    public static int Levenshtein(string a, string b)
    {
        if (a == b)
        {
            return 0;
        }

        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var prev = new int[b.Length + 1];
        var curr = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            curr[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, curr) = (curr, prev);
        }

        return prev[b.Length];
    }

    public static decimal LevenshteinRatio(string a, string b)
    {
        if (a.Length == 0 && b.Length == 0)
        {
            return 1m;
        }

        var max = Math.Max(a.Length, b.Length);
        return max == 0 ? 0m : 1m - (decimal)Levenshtein(a, b) / max;
    }

    private static string CollapseSpaces(string text)
        => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
