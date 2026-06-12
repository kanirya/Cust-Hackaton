namespace TaxNetGuardian.Api;

// ─────────────────────────────────────────────────────────────────────────────
// SANDBOX PROVIDER  (current MVP — reads from in-memory TaxNetState)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Reads from the synthetic in-memory Gov Data Sandbox.
/// Replace this with NadraGovernmentDataProvider / FbrGovernmentDataProvider
/// when official government APIs are available.
/// </summary>
public sealed class SandboxGovernmentDataProvider : IGovernmentDataProvider
{
    private readonly TaxNetState _state;

    public SandboxGovernmentDataProvider(TaxNetState state) => _state = state;

    public string ProviderCode => "SANDBOX";
    public string ProviderName  => "Gov Data Sandbox (Synthetic)";

    public Task<ProviderHealthResult> CheckHealthAsync(CancellationToken ct = default)
    {
        var healthy = _state.People.Count > 0;
        return Task.FromResult(new ProviderHealthResult(
            ProviderCode,
            healthy,
            healthy ? "Healthy" : "No data seeded",
            1,
            DateTimeOffset.UtcNow));
    }

    public Task<TaxProfile?> GetTaxProfileAsync(IdentityToken token, CancellationToken ct = default)
        => Task.FromResult<TaxProfile?>(
            _state.TaxProfiles.FirstOrDefault(x =>
                x.IdentityToken.Value.Equals(token.Value, StringComparison.OrdinalIgnoreCase)));

    public Task<SyntheticPerson?> GetIdentityProfileAsync(IdentityToken token, CancellationToken ct = default)
        => Task.FromResult<SyntheticPerson?>(
            _state.People.FirstOrDefault(x =>
                x.IdentityToken.Value.Equals(token.Value, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<VehicleRecord>> GetVehicleRecordsAsync(IdentityToken token, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<VehicleRecord>>(
            _state.Vehicles
                .Where(x => x.OwnerIdentityToken.Value.Equals(token.Value, StringComparison.OrdinalIgnoreCase))
                .ToArray());

    public Task<IReadOnlyList<PropertyRecord>> GetPropertyRecordsAsync(IdentityToken token, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<PropertyRecord>>(
            _state.Properties
                .Where(x => x.OwnerIdentityToken.Value.Equals(token.Value, StringComparison.OrdinalIgnoreCase))
                .ToArray());

    public Task<IReadOnlyList<BusinessRecord>> GetBusinessRecordsAsync(IdentityToken token, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<BusinessRecord>>(
            _state.Businesses
                .Where(x => x.RelatedIdentityToken.Value.Equals(token.Value, StringComparison.OrdinalIgnoreCase))
                .ToArray());

    public Task<IReadOnlyList<UtilityBillRecord>> GetUtilitySignalsAsync(IdentityToken token, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<UtilityBillRecord>>(
            _state.UtilityBills
                .Where(x => x.OwnerIdentityToken.Value.Equals(token.Value, StringComparison.OrdinalIgnoreCase))
                .ToArray());

    public Task<IReadOnlyList<TravelRecord>> GetTravelSignalsAsync(IdentityToken token, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<TravelRecord>>(
            _state.Travel
                .Where(x => x.TravelerIdentityToken.Value.Equals(token.Value, StringComparison.OrdinalIgnoreCase))
                .ToArray());
}

// ─────────────────────────────────────────────────────────────────────────────
// FUTURE OFFICIAL PROVIDERS  (stubs — not yet integrated)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Future official NADRA integration stub.
/// Requires approved API contract, credentials, audit controls, and legal authorization.
/// Register in GovernmentProviderRegistry and inject ISecretProvider + HttpClient.
/// </summary>
public sealed class NadraGovernmentDataProvider : IGovernmentDataProvider
{
    public string ProviderCode => "NADRA";
    public string ProviderName  => "NADRA (Official – Not Yet Integrated)";

    public Task<ProviderHealthResult> CheckHealthAsync(CancellationToken ct = default)
        => Task.FromResult(new ProviderHealthResult(
            ProviderCode, false, "NotIntegrated", 0, DateTimeOffset.UtcNow,
            "Official NADRA API not yet integrated. Implement NadraGovernmentDataProvider with approved credentials."));

    public Task<TaxProfile?>                GetTaxProfileAsync(IdentityToken t, CancellationToken ct = default)       => Task.FromResult<TaxProfile?>(null);
    public Task<SyntheticPerson?>           GetIdentityProfileAsync(IdentityToken t, CancellationToken ct = default)  => Task.FromResult<SyntheticPerson?>(null);
    public Task<IReadOnlyList<VehicleRecord>>      GetVehicleRecordsAsync(IdentityToken t, CancellationToken ct = default)   => Task.FromResult<IReadOnlyList<VehicleRecord>>([]);
    public Task<IReadOnlyList<PropertyRecord>>     GetPropertyRecordsAsync(IdentityToken t, CancellationToken ct = default)  => Task.FromResult<IReadOnlyList<PropertyRecord>>([]);
    public Task<IReadOnlyList<BusinessRecord>>     GetBusinessRecordsAsync(IdentityToken t, CancellationToken ct = default)  => Task.FromResult<IReadOnlyList<BusinessRecord>>([]);
    public Task<IReadOnlyList<UtilityBillRecord>>  GetUtilitySignalsAsync(IdentityToken t, CancellationToken ct = default)   => Task.FromResult<IReadOnlyList<UtilityBillRecord>>([]);
    public Task<IReadOnlyList<TravelRecord>>       GetTravelSignalsAsync(IdentityToken t, CancellationToken ct = default)    => Task.FromResult<IReadOnlyList<TravelRecord>>([]);
}

/// <summary>
/// Future official FBR integration stub.
/// </summary>
public sealed class FbrGovernmentDataProvider : IGovernmentDataProvider
{
    public string ProviderCode => "FBR";
    public string ProviderName  => "FBR (Official – Not Yet Integrated)";

    public Task<ProviderHealthResult> CheckHealthAsync(CancellationToken ct = default)
        => Task.FromResult(new ProviderHealthResult(
            ProviderCode, false, "NotIntegrated", 0, DateTimeOffset.UtcNow,
            "Official FBR API not yet integrated."));

    public Task<TaxProfile?>                GetTaxProfileAsync(IdentityToken t, CancellationToken ct = default)       => Task.FromResult<TaxProfile?>(null);
    public Task<SyntheticPerson?>           GetIdentityProfileAsync(IdentityToken t, CancellationToken ct = default)  => Task.FromResult<SyntheticPerson?>(null);
    public Task<IReadOnlyList<VehicleRecord>>      GetVehicleRecordsAsync(IdentityToken t, CancellationToken ct = default)   => Task.FromResult<IReadOnlyList<VehicleRecord>>([]);
    public Task<IReadOnlyList<PropertyRecord>>     GetPropertyRecordsAsync(IdentityToken t, CancellationToken ct = default)  => Task.FromResult<IReadOnlyList<PropertyRecord>>([]);
    public Task<IReadOnlyList<BusinessRecord>>     GetBusinessRecordsAsync(IdentityToken t, CancellationToken ct = default)  => Task.FromResult<IReadOnlyList<BusinessRecord>>([]);
    public Task<IReadOnlyList<UtilityBillRecord>>  GetUtilitySignalsAsync(IdentityToken t, CancellationToken ct = default)   => Task.FromResult<IReadOnlyList<UtilityBillRecord>>([]);
    public Task<IReadOnlyList<TravelRecord>>       GetTravelSignalsAsync(IdentityToken t, CancellationToken ct = default)    => Task.FromResult<IReadOnlyList<TravelRecord>>([]);
}

/// <summary>
/// Future provincial excise integration stub.
/// </summary>
public sealed class ExciseGovernmentDataProvider : IGovernmentDataProvider
{
    public string ProviderCode => "EXCISE";
    public string ProviderName  => "Excise (Official – Not Yet Integrated)";

    public Task<ProviderHealthResult> CheckHealthAsync(CancellationToken ct = default)
        => Task.FromResult(new ProviderHealthResult(
            ProviderCode, false, "NotIntegrated", 0, DateTimeOffset.UtcNow,
            "Provincial Excise API not yet integrated."));

    public Task<TaxProfile?>                GetTaxProfileAsync(IdentityToken t, CancellationToken ct = default)       => Task.FromResult<TaxProfile?>(null);
    public Task<SyntheticPerson?>           GetIdentityProfileAsync(IdentityToken t, CancellationToken ct = default)  => Task.FromResult<SyntheticPerson?>(null);
    public Task<IReadOnlyList<VehicleRecord>>      GetVehicleRecordsAsync(IdentityToken t, CancellationToken ct = default)   => Task.FromResult<IReadOnlyList<VehicleRecord>>([]);
    public Task<IReadOnlyList<PropertyRecord>>     GetPropertyRecordsAsync(IdentityToken t, CancellationToken ct = default)  => Task.FromResult<IReadOnlyList<PropertyRecord>>([]);
    public Task<IReadOnlyList<BusinessRecord>>     GetBusinessRecordsAsync(IdentityToken t, CancellationToken ct = default)  => Task.FromResult<IReadOnlyList<BusinessRecord>>([]);
    public Task<IReadOnlyList<UtilityBillRecord>>  GetUtilitySignalsAsync(IdentityToken t, CancellationToken ct = default)   => Task.FromResult<IReadOnlyList<UtilityBillRecord>>([]);
    public Task<IReadOnlyList<TravelRecord>>       GetTravelSignalsAsync(IdentityToken t, CancellationToken ct = default)    => Task.FromResult<IReadOnlyList<TravelRecord>>([]);
}

// ─────────────────────────────────────────────────────────────────────────────
// REGISTRY
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Resolves IGovernmentDataProvider by ProviderCode.
/// Add new provider implementations here as official APIs become available.
/// </summary>
public sealed class GovernmentProviderRegistry : IGovernmentProviderRegistry
{
    private readonly IReadOnlyDictionary<string, IGovernmentDataProvider> _providers;

    public GovernmentProviderRegistry(TaxNetState state)
    {
        var simulator = new SandboxFailureSimulator(state);
        var list = new IGovernmentDataProvider[]
        {
            // Wrap the sandbox provider so the canonical pipeline path honors active failure rules (Req 3).
            new FailureSimulatingGovernmentDataProvider(new SandboxGovernmentDataProvider(state), simulator),
            new NadraGovernmentDataProvider(),
            new FbrGovernmentDataProvider(),
            new ExciseGovernmentDataProvider()
        };
        _providers = list.ToDictionary(p => p.ProviderCode, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<IGovernmentDataProvider> GetAllProviders()
        => _providers.Values.ToArray();

    public IGovernmentDataProvider? GetProvider(string providerCode)
        => _providers.GetValueOrDefault(providerCode);
}
