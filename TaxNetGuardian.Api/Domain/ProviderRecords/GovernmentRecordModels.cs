namespace TaxNetGuardian.Api;

public sealed record TaxProfile(
    string ProviderRecordId,
    IdentityToken IdentityToken,
    string Ntn,
    string FilerStatus,
    decimal DeclaredAnnualIncome,
    decimal TaxPaid,
    int TaxYear,
    DateTimeOffset SourceUpdatedAtUtc);

public sealed record VehicleRecord(
    string ProviderRecordId,
    IdentityToken OwnerIdentityToken,
    string RegistrationNumberMasked,
    string Make,
    string Model,
    int EngineCc,
    int ModelYear,
    decimal EstimatedValue,
    string Province,
    DateTimeOffset SourceUpdatedAtUtc);

public sealed record PropertyRecord(
    string ProviderRecordId,
    IdentityToken OwnerIdentityToken,
    string PropertyToken,
    string City,
    string Area,
    string PropertyType,
    decimal EstimatedValue,
    DateTimeOffset SourceUpdatedAtUtc);

public sealed record UtilityBillRecord(
    string ProviderRecordId,
    IdentityToken OwnerIdentityToken,
    string MeterToken,
    string UtilityType,
    decimal AverageMonthlyBill,
    decimal LatestBillAmount,
    string City,
    DateTimeOffset SourceUpdatedAtUtc);

public sealed record BusinessRecord(
    string ProviderRecordId,
    string CompanyRegistrationNumber,
    string CompanyName,
    string RelationshipType,
    IdentityToken RelatedIdentityToken,
    string Status,
    DateTimeOffset SourceUpdatedAtUtc);

public sealed record TravelRecord(
    string ProviderRecordId,
    IdentityToken TravelerIdentityToken,
    string Destination,
    int TripsInLast24Months,
    decimal EstimatedSpend,
    DateTimeOffset SourceUpdatedAtUtc);

