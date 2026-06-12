using System.Globalization;

namespace TaxNetGuardian.Api;

/// <summary>
/// Sandbox Profile Editing and Asset Authoring state methods (Req 4).
/// All validation happens BEFORE any mutation, so a rejected request leaves the
/// <see cref="SyntheticPerson"/> and its asset collections completely unchanged (AC 8).
/// Every successful mutation takes <c>lock (_lock)</c>, audits, and snapshots.
/// The returned <c>Profile</c> object is the (possibly updated) <see cref="SyntheticPerson"/>;
/// the HTTP layer projects it through <c>BuildSandboxProfile</c> for the response body.
/// </summary>
public sealed partial class TaxNetState
{
    // AC 1,2,3,6,8,9 — validate every provided field, then apply atomically via `with { }`.
    public (ProfileEditOutcome Outcome, object? Profile, string? Error)
        PatchProfile(string syntheticPersonId, ProfilePatchRequest request, string actor)
    {
        lock (_lock)
        {
            if (request is null)
            {
                return (ProfileEditOutcome.ValidationError, null, "Profile patch request is required.");
            }

            // AC 3 — unknown profile: no mutation.
            var index = People.FindIndex(x =>
                x.Id.Equals(syntheticPersonId, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                return (ProfileEditOutcome.NotFound, null, null);
            }

            // AC 1,2,6 — validate ONLY the provided fields before touching state.
            var changedFields = new List<string>();

            foreach (var (name, value) in EnumerateProvidedTextFields(request))
            {
                if (!SandboxValidation.IsValidText(value))
                {
                    return (ProfileEditOutcome.ValidationError, null,
                        $"Field '{name}' must be between 1 and 256 characters.");
                }

                changedFields.Add(name);
            }

            if (request.ExpectedRiskBand is not null)
            {
                // AC 2,6 — case-sensitive membership in Low/Medium/High/Critical.
                if (!Array.Exists(SandboxValidation.RiskBands, b => string.Equals(b, request.ExpectedRiskBand, StringComparison.Ordinal)))
                {
                    return (ProfileEditOutcome.ValidationError, null,
                        $"Expected risk band must be one of {string.Join(", ", SandboxValidation.RiskBands)} (case-sensitive).");
                }

                changedFields.Add(nameof(request.ExpectedRiskBand));
            }

            // Validation complete — apply all provided fields atomically (AC 1).
            var existing = People[index];
            var updated = existing with
            {
                FullName = request.FullName ?? existing.FullName,
                UrduName = request.UrduName ?? existing.UrduName,
                FatherName = request.FatherName ?? existing.FatherName,
                City = request.City ?? existing.City,
                Province = request.Province ?? existing.Province,
                ExpectedRiskBand = request.ExpectedRiskBand ?? existing.ExpectedRiskBand
            };

            People[index] = updated;
            AddAuditEvent(actor, "taxnet-sandbox-admin", "SandboxProfilePatched", syntheticPersonId, "Succeeded", new Dictionary<string, object>
            {
                ["changedFields"] = changedFields.ToArray()
            });
            SaveSnapshot();
            return (ProfileEditOutcome.Updated, updated, null);
        }
    }

    // AC 3,4,5,7,8,9 — validate type/fields/limit, then add the typed asset record.
    public (ProfileEditOutcome Outcome, object? Profile, string? Error)
        AddProfileAsset(string syntheticPersonId, AssetAuthorRequest request, string actor)
    {
        lock (_lock)
        {
            if (request is null)
            {
                return (ProfileEditOutcome.ValidationError, null, "Asset author request is required.");
            }

            // AC 3 — unknown profile: no mutation.
            var person = People.FirstOrDefault(x =>
                x.Id.Equals(syntheticPersonId, StringComparison.OrdinalIgnoreCase));
            if (person is null)
            {
                return (ProfileEditOutcome.NotFound, null, null);
            }

            // AC 5 — asset type must be one of the supported types (case-insensitive).
            var assetType = request.AssetType?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(assetType) ||
                !Array.Exists(SandboxValidation.AssetTypes, t => string.Equals(t, assetType, StringComparison.Ordinal)))
            {
                return (ProfileEditOutcome.ValidationError, null,
                    $"Asset type must be one of {string.Join(", ", SandboxValidation.AssetTypes)}.");
            }

            // AC 5 — every provided field value must be 1..256 characters.
            var fields = request.Fields ?? new Dictionary<string, string>();
            foreach (var (key, value) in fields)
            {
                if (!SandboxValidation.IsValidText(value))
                {
                    return (ProfileEditOutcome.ValidationError, null,
                        $"Asset field '{key}' must be between 1 and 256 characters.");
                }
            }

            // AC 7 — enforce the per-type limit before mutating.
            var token = person.IdentityToken;
            var currentCount = CountAssetsOfType(assetType, token);
            if (currentCount >= SandboxValidation.MaxAssetsPerType)
            {
                return (ProfileEditOutcome.LimitReached, null,
                    $"Profile already holds the maximum of {SandboxValidation.MaxAssetsPerType} {assetType} assets.");
            }

            // Validation complete — construct and append the concrete record (AC 4).
            AppendAsset(assetType, token, fields);
            AddAuditEvent(actor, "taxnet-sandbox-admin", "SandboxAssetAdded", syntheticPersonId, "Succeeded", new Dictionary<string, object>
            {
                ["assetType"] = assetType
            });
            SaveSnapshot();
            return (ProfileEditOutcome.Updated, person, null);
        }
    }

    private static IEnumerable<(string Name, string? Value)> EnumerateProvidedTextFields(ProfilePatchRequest request)
    {
        if (request.FullName is not null) yield return (nameof(request.FullName), request.FullName);
        if (request.UrduName is not null) yield return (nameof(request.UrduName), request.UrduName);
        if (request.FatherName is not null) yield return (nameof(request.FatherName), request.FatherName);
        if (request.City is not null) yield return (nameof(request.City), request.City);
        if (request.Province is not null) yield return (nameof(request.Province), request.Province);
    }

    private int CountAssetsOfType(string assetType, IdentityToken token) => assetType switch
    {
        "vehicle" => Vehicles.Count(x => x.OwnerIdentityToken.Value == token.Value),
        "property" => Properties.Count(x => x.OwnerIdentityToken.Value == token.Value),
        "utility" => UtilityBills.Count(x => x.OwnerIdentityToken.Value == token.Value),
        "business" => Businesses.Count(x => x.RelatedIdentityToken.Value == token.Value),
        "travel" => Travel.Count(x => x.TravelerIdentityToken.Value == token.Value),
        "taxreturn" => TaxProfiles.Count(x => x.IdentityToken.Value == token.Value),
        _ => 0
    };

    private void AppendAsset(string assetType, IdentityToken token, IReadOnlyDictionary<string, string> fields)
    {
        var now = DateTimeOffset.UtcNow;
        var recordId = $"{assetType}-{Guid.NewGuid():N}";

        switch (assetType)
        {
            case "vehicle":
                Vehicles.Add(new VehicleRecord(
                    recordId,
                    token,
                    Field(fields, "registrationNumberMasked", "RegistrationNumber", "registration"),
                    Field(fields, "make"),
                    Field(fields, "model"),
                    FieldInt(fields, "engineCc"),
                    FieldInt(fields, "modelYear"),
                    FieldDecimal(fields, "estimatedValue"),
                    Field(fields, "province"),
                    now));
                break;
            case "property":
                Properties.Add(new PropertyRecord(
                    recordId,
                    token,
                    Field(fields, "propertyToken", "token"),
                    Field(fields, "city"),
                    Field(fields, "area"),
                    Field(fields, "propertyType", "type"),
                    FieldDecimal(fields, "estimatedValue"),
                    now));
                break;
            case "utility":
                UtilityBills.Add(new UtilityBillRecord(
                    recordId,
                    token,
                    Field(fields, "meterToken", "meter"),
                    Field(fields, "utilityType", "type"),
                    FieldDecimal(fields, "averageMonthlyBill"),
                    FieldDecimal(fields, "latestBillAmount"),
                    Field(fields, "city"),
                    now));
                break;
            case "business":
                Businesses.Add(new BusinessRecord(
                    recordId,
                    Field(fields, "companyRegistrationNumber", "registrationNumber"),
                    Field(fields, "companyName", "name"),
                    Field(fields, "relationshipType", "relationship"),
                    token,
                    Field(fields, "status"),
                    now));
                break;
            case "travel":
                Travel.Add(new TravelRecord(
                    recordId,
                    token,
                    Field(fields, "destination"),
                    FieldInt(fields, "tripsInLast24Months", "trips"),
                    FieldDecimal(fields, "estimatedSpend"),
                    now));
                break;
            case "taxreturn":
                TaxProfiles.Add(new TaxProfile(
                    recordId,
                    token,
                    Field(fields, "ntn"),
                    Field(fields, "filerStatus", "status"),
                    FieldDecimal(fields, "declaredAnnualIncome", "income"),
                    FieldDecimal(fields, "taxPaid"),
                    FieldInt(fields, "taxYear"),
                    now));
                break;
        }
    }

    // Case-insensitive field lookup over the supplied aliases; empty string when absent.
    private static string Field(IReadOnlyDictionary<string, string> fields, params string[] keys)
    {
        foreach (var key in keys)
        {
            foreach (var (k, v) in fields)
            {
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                {
                    return v;
                }
            }
        }

        return string.Empty;
    }

    private static int FieldInt(IReadOnlyDictionary<string, string> fields, params string[] keys)
        => int.TryParse(Field(fields, keys), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    private static decimal FieldDecimal(IReadOnlyDictionary<string, string> fields, params string[] keys)
        => decimal.TryParse(Field(fields, keys), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : 0m;
}
