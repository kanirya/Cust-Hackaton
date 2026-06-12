namespace TaxNetGuardian.Api;

public sealed partial class TaxNetState
{
    /// <summary>
    /// Universal identity search. Given a CNIC (Pakistan national ID), name, NTN, identity token,
    /// person id, or case id, resolves the citizen and consolidates every record linked to them
    /// across NADRA, FBR, Excise, SECP, property, utility, and travel sources — then surfaces
    /// "possible same identity" candidates (the same person registered under a different name
    /// spelling in another source) using the entity-resolution similarity engine.
    /// </summary>
    public IdentitySearchResponse SearchIdentities(string query, int limit = 25)
    {
        var q = (query ?? "").Trim();
        if (q.Length == 0)
        {
            return new IdentitySearchResponse(q, 0, [], "Enter a CNIC, name, NTN, identity token, or case ID to resolve a citizen across all sources.");
        }

        var qLower = q.ToLowerInvariant();
        var qDigits = new string(q.Where(char.IsDigit).ToArray());
        var matches = new List<IdentitySearchMatch>();

        foreach (var person in People)
        {
            var tax = TaxProfiles.FirstOrDefault(x => x.IdentityToken.Value == person.IdentityToken.Value);
            var caseItem = Cases.FirstOrDefault(c => c.PersonId == person.Id);

            var matchedOn = MatchReasonFor(person, tax, caseItem, qLower, qDigits);
            if (matchedOn is null)
            {
                continue;
            }

            matches.Add(BuildIdentityMatch(person, tax, caseItem, matchedOn));
            if (matches.Count >= Math.Clamp(limit, 1, 100))
            {
                break;
            }
        }

        // Strongest matches first: prefer those with an open case and higher risk.
        matches = matches
            .OrderByDescending(m => m.Case is not null)
            .ThenByDescending(m => m.Case?.Score ?? 0)
            .ToList();

        var explanation = matches.Count == 0
            ? $"No identity matched '{q}'. Try a CNIC fragment (e.g. 42201), a full name, an NTN, or a case ID."
            : $"Resolved {matches.Count} identity record(s) for '{q}'. Records are linked across NADRA, FBR, Excise, SECP, property, utility, and travel sources keyed on the citizen identity token; name-variant candidates are flagged as possible same identity.";

        return new IdentitySearchResponse(q, matches.Count, matches, explanation);
    }

    private static string? MatchReasonFor(SyntheticPerson person, TaxProfile? tax, CaseItem? caseItem, string qLower, string qDigits)
    {
        var cnicDigits = new string(person.CnicMasked.Where(char.IsDigit).ToArray());
        if (qDigits.Length >= 3 && cnicDigits.Contains(qDigits))
        {
            return "CNIC";
        }

        if (person.CnicMasked.ToLowerInvariant().Contains(qLower))
        {
            return "CNIC";
        }

        if (!string.IsNullOrWhiteSpace(tax?.Ntn) && tax!.Ntn.ToLowerInvariant().Contains(qLower))
        {
            return "NTN";
        }

        if (person.IdentityToken.Value.ToLowerInvariant().Contains(qLower))
        {
            return "Identity token";
        }

        if (caseItem is not null && caseItem.Id.ToLowerInvariant().Contains(qLower))
        {
            return "Case ID";
        }

        if (person.FullName.ToLowerInvariant().Contains(qLower) || person.Id.ToLowerInvariant().Contains(qLower))
        {
            return "Name";
        }

        // Fuzzy: a noisy/alternate spelling of the queried name still resolves the citizen.
        if (qLower.Length >= 4 && IdentityResolutionEngine.JaroWinklerSimilarity(person.FullName, qLower) >= 0.86m)
        {
            return "Name (fuzzy)";
        }

        return null;
    }

    private IdentitySearchMatch BuildIdentityMatch(SyntheticPerson person, TaxProfile? tax, CaseItem? caseItem, string matchedOn)
    {
        var token = person.IdentityToken.Value;
        var linked = new LinkedRecordSummary(
            TaxProfiles.Count(x => x.IdentityToken.Value == token),
            Vehicles.Count(x => x.OwnerIdentityToken.Value == token),
            Properties.Count(x => x.OwnerIdentityToken.Value == token),
            UtilityBills.Count(x => x.OwnerIdentityToken.Value == token),
            Businesses.Count(x => x.RelatedIdentityToken.Value == token),
            Travel.Count(x => x.TravelerIdentityToken.Value == token));

        var entity = Entities.FirstOrDefault(e => e.PersonId == person.Id);
        var entitySummary = entity is null
            ? null
            : new ResolvedEntitySummary(entity.Id, entity.MatchConfidence, entity.RequiresHumanReview, entity.MatchReasons, entity.LinkedRecordIds.Count);

        var caseSummary = caseItem is null
            ? null
            : new CaseSummary(caseItem.Id, caseItem.Score.Score, caseItem.Score.RiskBand, caseItem.Status);

        var variants = People
            .Where(other => other.Id != person.Id)
            .Select(other =>
            {
                var similarity = IdentityResolutionEngine.JaroWinklerSimilarity(other.FullName, person.FullName);
                var sameFatherCity = !string.IsNullOrWhiteSpace(person.FatherName)
                    && string.Equals(other.FatherName, person.FatherName, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(other.City, person.City, StringComparison.OrdinalIgnoreCase);
                string? reason = sameFatherCity
                    ? $"Same father ({person.FatherName}) and city ({person.City}) registered under a different name"
                    : similarity >= 0.86m
                        ? $"Name spelling similarity {similarity:P0}"
                        : null;
                return (other, similarity, reason);
            })
            .Where(x => x.reason is not null)
            .OrderByDescending(x => x.similarity)
            .Take(5)
            .Select(x => new IdentityVariant(x.other.Id, x.other.FullName, x.other.CnicMasked, x.other.City, x.similarity, x.reason!))
            .ToArray();

        return new IdentitySearchMatch(
            person.Id,
            person.FullName,
            person.FatherName,
            person.CnicMasked,
            person.City,
            person.Province,
            token,
            tax?.Ntn,
            tax?.FilerStatus,
            matchedOn,
            linked,
            entitySummary,
            caseSummary,
            variants);
    }
}

public sealed record IdentitySearchResponse(
    string Query,
    int Total,
    IReadOnlyList<IdentitySearchMatch> Matches,
    string Explanation);

public sealed record IdentitySearchMatch(
    string PersonId,
    string FullName,
    string FatherName,
    string CnicMasked,
    string City,
    string Province,
    string IdentityToken,
    string? Ntn,
    string? FilerStatus,
    string MatchedOn,
    LinkedRecordSummary LinkedRecords,
    ResolvedEntitySummary? Entity,
    CaseSummary? Case,
    IReadOnlyList<IdentityVariant> PossibleSameIdentity);

public sealed record LinkedRecordSummary(int Tax, int Vehicles, int Properties, int Utilities, int Businesses, int Travel)
{
    public int Total => Tax + Vehicles + Properties + Utilities + Businesses + Travel;
}

public sealed record ResolvedEntitySummary(
    string EntityId,
    decimal MatchConfidence,
    bool RequiresHumanReview,
    IReadOnlyList<string> MatchReasons,
    int LinkedRecordCount);

public sealed record CaseSummary(string CaseId, int Score, string RiskBand, string Status);

public sealed record IdentityVariant(
    string PersonId,
    string FullName,
    string CnicMasked,
    string City,
    decimal NameSimilarity,
    string Reason);
