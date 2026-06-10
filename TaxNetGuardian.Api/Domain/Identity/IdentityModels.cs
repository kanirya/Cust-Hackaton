namespace TaxNetGuardian.Api;

public sealed record IdentityToken(string Value, string TokenType, string Issuer, bool IsSynthetic);

public sealed record SyntheticPerson(
    string Id,
    string FullName,
    string UrduName,
    string FatherName,
    string City,
    string Province,
    string CnicMasked,
    string PhoneMasked,
    IdentityToken IdentityToken,
    string ExpectedRiskBand);

public sealed record ResolvedEntity(
    string Id,
    string PersonId,
    decimal MatchConfidence,
    IReadOnlyList<string> LinkedRecordIds,
    IReadOnlyList<string> MatchReasons,
    bool RequiresHumanReview);

