namespace TaxNetGuardian.Api;

/// <summary>
/// Canonical interface for all government data providers.
/// 
/// Production path:
///   SandboxGovernmentDataProvider  → uses synthetic in-memory data (current MVP)
///   NadraGovernmentDataProvider    → future official NADRA integration
///   FbrGovernmentDataProvider      → future official FBR integration
///   ExciseGovernmentDataProvider   → future provincial excise integration
/// 
/// The intelligence pipeline always works through this interface.
/// No service depends on sandbox-specific DTOs.
/// Swap the implementation in GovernmentProviderRegistry when official APIs are approved.
/// </summary>
public interface IGovernmentDataProvider
{
    string ProviderCode { get; }
    string ProviderName { get; }

    Task<ProviderHealthResult> CheckHealthAsync(CancellationToken ct = default);
    Task<TaxProfile?> GetTaxProfileAsync(IdentityToken token, CancellationToken ct = default);
    Task<SyntheticPerson?> GetIdentityProfileAsync(IdentityToken token, CancellationToken ct = default);
    Task<IReadOnlyList<VehicleRecord>> GetVehicleRecordsAsync(IdentityToken token, CancellationToken ct = default);
    Task<IReadOnlyList<PropertyRecord>> GetPropertyRecordsAsync(IdentityToken token, CancellationToken ct = default);
    Task<IReadOnlyList<BusinessRecord>> GetBusinessRecordsAsync(IdentityToken token, CancellationToken ct = default);
    Task<IReadOnlyList<UtilityBillRecord>> GetUtilitySignalsAsync(IdentityToken token, CancellationToken ct = default);
    Task<IReadOnlyList<TravelRecord>> GetTravelSignalsAsync(IdentityToken token, CancellationToken ct = default);
}

/// <summary>
/// Registry that resolves providers by code.
/// Register new provider implementations here when official APIs become available.
/// </summary>
public interface IGovernmentProviderRegistry
{
    IReadOnlyList<IGovernmentDataProvider> GetAllProviders();
    IGovernmentDataProvider? GetProvider(string providerCode);
}

public sealed record ProviderHealthResult(
    string ProviderCode,
    bool IsHealthy,
    string Status,
    int LatencyMs,
    DateTimeOffset CheckedAtUtc,
    string? Error = null);
