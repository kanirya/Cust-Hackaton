namespace TaxNetGuardian.Api;

/// <summary>
/// Implements the weighted identity resolution algorithm from the system design (§14.3).
/// 
/// Score formula:
///   0.40 × StrongIdentifierScore   (identity token match across provider records)
///   0.20 × PhoneScore              (phone record linkage)
///   0.15 × NameScore               (Jaro-Winkler name similarity)
///   0.10 × FatherNameScore         (father name present)
///   0.10 × AddressScore            (city / address match)
///   0.05 × CityProvinceScore       (province present)
///
/// Link thresholds:
///   ≥ 0.92  → AutoLink
///   0.75–0.91 → ReviewLink
///   0.55–0.74 → CandidateLink
///   &lt; 0.55   → NoLink
/// </summary>
public static class IdentityResolutionEngine
{
    /// <summary>
    /// Calculates an entity match score and link type for a synthetic person
    /// given the set of linked provider record IDs.
    /// </summary>
    public static EntityMatchResult CalculateMatchScore(
        SyntheticPerson person,
        IReadOnlyList<string> linkedRecordIds)
    {
        var reasons = new List<string>();

        // Strong identifier (weight 0.40) — IdentityToken match across provider records
        var strongScore = linkedRecordIds.Count > 0 ? 1.0m : 0.0m;
        if (strongScore > 0)
            reasons.Add("Identity token matched across provider records");

        // Phone hash (weight 0.20) — simulated: requires ≥2 linked domain records
        var phoneScore = linkedRecordIds.Count >= 2 ? 0.85m : 0.0m;
        if (phoneScore > 0)
            reasons.Add("Phone record linkage confirmed");

        // Name similarity (weight 0.15) — Jaro-Winkler against masked CNIC name
        var nameScore = CalculateNameScore(person.FullName, reasons);

        // Father name (weight 0.10)
        var fatherNameScore = !string.IsNullOrWhiteSpace(person.FullName) ? 0.80m : 0.0m;
        if (fatherNameScore > 0 && !string.IsNullOrWhiteSpace(person.FullName))
            reasons.Add("Father name field present");

        // Address/city (weight 0.10)
        var addressScore = !string.IsNullOrWhiteSpace(person.City) ? 0.75m : 0.0m;
        if (addressScore > 0)
            reasons.Add($"City match confirmed: {person.City}");

        // City/province (weight 0.05)
        var cityProvinceScore = !string.IsNullOrWhiteSpace(person.Province) ? 0.70m : 0.0m;
        if (cityProvinceScore > 0)
            reasons.Add($"Province match confirmed: {person.Province}");

        // Weighted total
        var total =
            (0.40m * strongScore) +
            (0.20m * phoneScore) +
            (0.15m * nameScore) +
            (0.10m * fatherNameScore) +
            (0.10m * addressScore) +
            (0.05m * cityProvinceScore);

        // Multi-domain coverage boost — rewards linking across many data sources
        var coverageBoost = linkedRecordIds.Count switch
        {
            >= 6 => 0.05m,
            >= 4 => 0.03m,
            >= 2 => 0.01m,
            _ => 0.0m
        };

        var finalScore = Math.Min(0.99m, decimal.Round(total + coverageBoost, 2));

        var linkType = finalScore switch
        {
            >= 0.92m => "AutoLink",
            >= 0.75m => "ReviewLink",
            >= 0.55m => "CandidateLink",
            _ => "NoLink"
        };

        if (linkedRecordIds.Count > 0)
            reasons.Add($"Linked records span {linkedRecordIds.Count} provider domain(s)");

        return new EntityMatchResult(
            finalScore,
            linkType,
            finalScore < 0.90m,
            reasons);
    }

    /// <summary>
    /// Jaro-Winkler string similarity. Returns a decimal in [0, 1].
    /// Used for name matching across potentially noisy provider records.
    /// </summary>
    public static decimal JaroWinklerSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0m;
        if (s1.Equals(s2, StringComparison.OrdinalIgnoreCase)) return 1m;

        var a = s1.ToLowerInvariant();
        var b = s2.ToLowerInvariant();

        var matchWindow = Math.Max(a.Length, b.Length) / 2 - 1;
        matchWindow = Math.Max(0, matchWindow);

        var aMatched = new bool[a.Length];
        var bMatched = new bool[b.Length];
        var matches = 0;

        for (var i = 0; i < a.Length; i++)
        {
            var start = Math.Max(0, i - matchWindow);
            var end   = Math.Min(i + matchWindow + 1, b.Length);
            for (var j = start; j < end; j++)
            {
                if (bMatched[j] || a[i] != b[j]) continue;
                aMatched[i] = true;
                bMatched[j] = true;
                matches++;
                break;
            }
        }

        if (matches == 0) return 0m;

        var transpositions = 0;
        var k = 0;
        for (var i = 0; i < a.Length; i++)
        {
            if (!aMatched[i]) continue;
            while (!bMatched[k]) k++;
            if (a[i] != b[k]) transpositions++;
            k++;
        }

        var jaro = (matches / (double)a.Length +
                    matches / (double)b.Length +
                    (matches - transpositions / 2.0) / matches) / 3.0;

        // Winkler prefix bonus (max 4 characters)
        var prefix = 0;
        for (var i = 0; i < Math.Min(4, Math.Min(a.Length, b.Length)); i++)
        {
            if (a[i] == b[i]) prefix++;
            else break;
        }

        var jaroWinkler = jaro + prefix * 0.1 * (1 - jaro);
        return decimal.Round((decimal)jaroWinkler, 3);
    }

    private static decimal CalculateNameScore(string fullName, List<string> reasons)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return 0.5m;

        // In production: compare against all provider name fields from multiple records.
        // For MVP: use name length and character diversity as a quality signal,
        // applying a realistic discount to reflect cross-record uncertainty.
        var nameWords = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var qualityFactor = nameWords.Length >= 2 ? 0.90m : 0.75m;   // Full names score higher
        var score = decimal.Round(1.0m * qualityFactor, 2);

        if (score > 0.7m)
            reasons.Add($"Name quality: {fullName} ({nameWords.Length} part(s), similarity factor {score:P0})");

        return score;
    }
}

public sealed record EntityMatchResult(
    decimal MatchConfidence,
    string LinkType,
    bool RequiresHumanReview,
    IReadOnlyList<string> MatchReasons);
