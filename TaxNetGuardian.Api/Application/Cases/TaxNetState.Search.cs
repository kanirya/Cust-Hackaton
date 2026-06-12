namespace TaxNetGuardian.Api;

public sealed partial class TaxNetState
{
    // High-accuracy unified search across people, CNIC identity tokens, NTN, profile IDs,
    // case IDs, and (fuzzy) names. Every candidate is scored 0..1 and ranked, so the global
    // command box resolves to the right subject even with partial, masked, or noisy input.
    public SearchResponse SearchEntities(string query, int limit = 8)
    {
        var raw = (query ?? "").Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return new SearchResponse(raw, 0, []);
        }

        var qNorm = NormalizeCnic(raw);
        var qDigits = DigitsOnly(raw);
        var qLower = raw.ToLowerInvariant();
        var max = Math.Clamp(limit, 1, 25);

        var hits = new List<SearchHit>();
        foreach (var person in People)
        {
            var caseItem = Cases.FirstOrDefault(c => c.PersonId.Equals(person.Id, StringComparison.OrdinalIgnoreCase));
            var ntn = TaxProfiles.FirstOrDefault(t => t.IdentityToken.Value == person.IdentityToken.Value)?.Ntn;
            var (confidence, matchType) = ScorePersonMatch(person, caseItem, raw, qLower, qNorm, qDigits, ntn);
            if (confidence < 0.45m)
            {
                continue;
            }

            hits.Add(new SearchHit(
                "person",
                matchType,
                decimal.Round(confidence, 3),
                person.Id,
                caseItem?.Id,
                person.FullName,
                person.UrduName,
                person.FatherName,
                person.CnicMasked,
                ntn,
                person.City,
                person.Province,
                caseItem?.Score.RiskBand,
                caseItem is null ? null : (int)caseItem.Score.Score,
                $"{person.FullName} · {person.City}, {person.Province} · {person.CnicMasked}"));
        }

        var ordered = hits
            .OrderByDescending(h => h.Confidence)
            .ThenByDescending(h => h.CaseId != null)
            .ThenByDescending(h => h.Score ?? 0)
            .Take(max)
            .ToArray();

        return new SearchResponse(raw, ordered.Length, ordered);
    }

    // Returns the single most-confident person match for a free-text query (CNIC, NTN, ID, or name),
    // or null when nothing crosses the confidence bar. Used to resolve global search to a subject.
    public SyntheticPerson? ResolveBestPerson(string query)
    {
        var result = SearchEntities(query, 1);
        if (result.Items.Count == 0)
        {
            return null;
        }

        var top = result.Items[0];
        return top.Confidence >= 0.6m
            ? People.FirstOrDefault(p => p.Id.Equals(top.PersonId, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    private (decimal Confidence, string MatchType) ScorePersonMatch(
        SyntheticPerson person,
        CaseItem? caseItem,
        string raw,
        string qLower,
        string qNorm,
        string qDigits,
        string? ntn)
    {
        // Case ID is an exact, unambiguous handle — rank it at the top.
        if (caseItem is not null &&
            (caseItem.Id.Equals(raw, StringComparison.OrdinalIgnoreCase) ||
             caseItem.Id.Equals($"case-{raw}", StringComparison.OrdinalIgnoreCase)))
        {
            return (1.0m, "Case ID exact match");
        }

        var best = 0m;
        var type = "";

        // --- CNIC (digit-aware, masked-aware) ---
        var pNorm = NormalizeCnic(person.CnicMasked);
        var pDigits = DigitsOnly(person.CnicMasked);
        if (qDigits.Length >= 5)
        {
            if (pNorm.Equals(qNorm, StringComparison.OrdinalIgnoreCase) ||
                (qDigits.Length >= 13 && pDigits.Equals(qDigits, StringComparison.Ordinal)) ||
                person.IdentityToken.Value.Equals(raw, StringComparison.OrdinalIgnoreCase))
            {
                return (1.0m, "CNIC exact match");
            }

            if (pDigits.Length > 0 && qDigits.Length >= 7 && pDigits.Contains(qDigits, StringComparison.Ordinal))
            {
                Consider(ref best, ref type, 0.96m, "CNIC partial match");
            }

            if (pDigits.Length >= 6 && qDigits.Length >= 6 &&
                pDigits[..5].Equals(qDigits[..5], StringComparison.Ordinal) &&
                pDigits[^1] == qDigits[^1])
            {
                Consider(ref best, ref type, 0.9m, "CNIC block + check digit");
            }
        }

        // --- NTN ---
        if (!string.IsNullOrWhiteSpace(ntn) && qLower.Length >= 3)
        {
            var nLower = ntn.ToLowerInvariant();
            if (nLower.Equals(qLower, StringComparison.Ordinal))
            {
                return (0.99m, "NTN exact match");
            }

            if (nLower.Contains(qLower, StringComparison.Ordinal))
            {
                Consider(ref best, ref type, 0.85m, "NTN match");
            }
        }

        // --- Profile ID ---
        if (qLower.Length >= 2)
        {
            var idLower = person.Id.ToLowerInvariant();
            if (idLower.Equals(qLower, StringComparison.Ordinal))
            {
                return (0.98m, "Profile ID exact match");
            }

            if (qLower.Length >= 3 && idLower.Contains(qLower, StringComparison.Ordinal))
            {
                Consider(ref best, ref type, 0.8m, "Profile ID match");
            }
        }

        // --- Names (full, father, Urdu) — variant/Urdu/cross-script aware ---
        Consider(ref best, ref type, ScoreNameAdvanced(person.FullName, person.UrduName, raw), "Name match");
        var fatherScore = ScoreNameAdvanced(person.FatherName, null, raw);
        if (fatherScore > 0m)
        {
            Consider(ref best, ref type, fatherScore * 0.9m, "Father name match");
        }

        // --- Location (weak, only when nothing stronger matched) ---
        if (qLower.Length >= 3 && best < 0.5m)
        {
            if (person.City.ToLowerInvariant().Contains(qLower, StringComparison.Ordinal))
            {
                Consider(ref best, ref type, 0.5m, $"City: {person.City}");
            }
            else if (person.Province.ToLowerInvariant().Contains(qLower, StringComparison.Ordinal))
            {
                Consider(ref best, ref type, 0.45m, $"Province: {person.Province}");
            }
        }

        return (best, type);
    }

    private static void Consider(ref decimal best, ref string type, decimal candidate, string candidateType)
    {
        if (candidate > best)
        {
            best = candidate;
            type = candidateType;
        }
    }

    // Comprehensive name relevance across scripts and spellings. Compares the query against the
    // person's Latin name, romanized Urdu name, and Urdu name directly, using exact / token-set /
    // substring / fuzzy (Jaro-Winkler + Levenshtein) over both raw and spelling-canonical forms.
    // Returns 0..~0.99. Designed so "Mohd Ali", "محمد علی", and "Muhammad Ali Khan" all resolve.
    private static decimal ScoreNameAdvanced(string? latinName, string? urduName, string rawQuery)
    {
        if (string.IsNullOrWhiteSpace(rawQuery))
        {
            return 0m;
        }

        var queryIsUrdu = NameMatching.ContainsUrduScript(rawQuery);

        // Build the query's comparable Latin form: romanize Urdu queries, normalize Latin ones.
        var qLatin = queryIsUrdu ? NameMatching.RomanizeUrdu(rawQuery) : NameMatching.NormalizeLatin(rawQuery);
        if (qLatin.Length < 2 && !queryIsUrdu)
        {
            return 0m;
        }

        var best = 0m;

        // 1) Direct Urdu-vs-Urdu comparison (normalized) when both sides have Urdu.
        if (queryIsUrdu && !string.IsNullOrWhiteSpace(urduName))
        {
            var qU = NameMatching.NormalizeUrdu(rawQuery);
            var cU = NameMatching.NormalizeUrdu(urduName);
            if (qU.Length > 0 && cU.Length > 0)
            {
                if (cU.Equals(qU, StringComparison.Ordinal))
                {
                    return 0.99m;
                }

                if (cU.Contains(qU, StringComparison.Ordinal))
                {
                    best = Math.Max(best, 0.95m);
                }

                best = Math.Max(best, IdentityResolutionEngine.JaroWinklerSimilarity(cU, qU) >= 0.86m
                    ? Math.Min(0.92m, IdentityResolutionEngine.JaroWinklerSimilarity(cU, qU))
                    : 0m);
            }
        }

        // 2) Latin comparisons against the Latin name AND the romanized Urdu name.
        foreach (var candidateRaw in new[] { latinName, NameMatching.RomanizeUrdu(urduName) })
        {
            var cLatin = NameMatching.NormalizeLatin(candidateRaw);
            if (cLatin.Length == 0)
            {
                continue;
            }

            best = Math.Max(best, ScoreLatinPair(cLatin, qLatin));
            if (best >= 0.99m)
            {
                break;
            }
        }

        return best;
    }

    // Score two normalized Latin strings: exact > canonical token-set > subset > substring > fuzzy.
    private static decimal ScoreLatinPair(string cLatin, string qLatin)
    {
        if (cLatin.Equals(qLatin, StringComparison.Ordinal))
        {
            return 0.99m;
        }

        var qTokens = NameMatching.Tokenize(qLatin);
        var cTokens = NameMatching.Tokenize(cLatin);
        if (qTokens.Length == 0 || cTokens.Length == 0)
        {
            return 0m;
        }

        var qCanon = NameMatching.CanonTokens(qTokens);
        var cCanon = NameMatching.CanonTokens(cTokens);
        var qSet = new HashSet<string>(qCanon, StringComparer.Ordinal);
        var cSet = new HashSet<string>(cCanon, StringComparer.Ordinal);

        // Canonical token-set equality (order-independent): "Mohd Ali Khan" == "Muhammad Ali Khan".
        if (qSet.Count > 0 && qSet.SetEquals(cSet))
        {
            return 0.98m;
        }

        // All query tokens present in candidate (canonical) — strong subset match.
        var matched = qSet.Count(q => cSet.Contains(q));
        if (matched == qSet.Count)
        {
            // Require either >1 token or a reasonably distinctive single token to avoid "khan"-only noise.
            if (qSet.Count >= 2 || qLatin.Length >= 4)
            {
                return 0.92m;
            }

            return 0.62m;
        }

        // Partial token overlap with per-token fuzzy (handles one typo'd token + transposition).
        if (qSet.Count >= 2)
        {
            var fuzzyMatched = 0;
            foreach (var q in qCanon)
            {
                var tokenBest = cCanon.Length == 0 ? 0m : cCanon.Max(c =>
                {
                    var jw = IdentityResolutionEngine.JaroWinklerSimilarity(c, q);
                    var lev = NameMatching.LevenshteinRatio(c, q);
                    var skel = NameMatching.ConsonantSkeleton(c).Equals(NameMatching.ConsonantSkeleton(q), StringComparison.Ordinal) ? 0.9m : 0m;
                    return Math.Max(Math.Max(jw, lev), skel);
                });
                if (tokenBest >= 0.84m)
                {
                    fuzzyMatched++;
                }
            }

            if (fuzzyMatched == qSet.Count)
            {
                return 0.88m;
            }

            if (fuzzyMatched >= 1 && qSet.Count >= 2)
            {
                return 0.6m + 0.1m * fuzzyMatched / qSet.Count;
            }
        }

        // Substring (e.g. searching a single distinctive token).
        if (cLatin.Contains(qLatin, StringComparison.Ordinal) && qLatin.Length >= 4)
        {
            return 0.86m;
        }

        // Whole-string + best-token fuzzy fallback.
        var canonWhole = string.Join(' ', cCanon);
        var qCanonWhole = string.Join(' ', qCanon);
        var wholeJw = IdentityResolutionEngine.JaroWinklerSimilarity(canonWhole, qCanonWhole);
        var tokenJw = cCanon.Length == 0 ? 0m : cCanon.Max(c => IdentityResolutionEngine.JaroWinklerSimilarity(c, qLatin));
        var levWhole = NameMatching.LevenshteinRatio(canonWhole, qCanonWhole);
        var sim = Math.Max(Math.Max(wholeJw, tokenJw), levWhole);
        return sim >= 0.84m ? Math.Min(0.85m, sim) : 0m;
    }
}
