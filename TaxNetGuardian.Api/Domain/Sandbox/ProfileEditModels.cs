namespace TaxNetGuardian.Api;

public sealed record ProfilePatchRequest(
    string? FullName, string? UrduName, string? FatherName,
    string? City, string? Province, string? ExpectedRiskBand);

public sealed record AssetAuthorRequest(
    string AssetType,                 // vehicle|property|utility|business|travel|taxreturn (case-insensitive)
    IReadOnlyDictionary<string, string> Fields);   // each value validated 1..256 chars

public enum ProfileEditOutcome { Updated, NotFound, ValidationError, LimitReached }

public static class SandboxValidation
{
    public static readonly string[] RiskBands = ["Low", "Medium", "High", "Critical"]; // case-sensitive (AC 2,6)
    public static readonly string[] AssetTypes = ["vehicle", "property", "utility", "business", "travel", "taxreturn"];
    public const int MaxAssetsPerType = 100;          // AC 7
    public static bool IsValidText(string? v) => v is { Length: >= 1 and <= 256 }; // AC 1,4,5
}
