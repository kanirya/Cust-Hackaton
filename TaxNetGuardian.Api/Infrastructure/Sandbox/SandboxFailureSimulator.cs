namespace TaxNetGuardian.Api;

/// <summary>
/// Thrown when a sandbox failure rule with <see cref="FailureBehavior.Offline"/> is active for a
/// provider. Callers map this to HTTP 503 (AC 7); the canonical pipeline treats it as a provider failure.
/// </summary>
public sealed class SandboxOfflineException : Exception
{
    public SandboxOfflineException(string providerCode)
        : base($"Sandbox provider '{providerCode}' is offline (failure rule).")
        => ProviderCode = providerCode;

    public string ProviderCode { get; }
}

/// <summary>
/// Thrown when a sandbox failure rule maps to a provider-side fault that callers translate to
/// HTTP 429 (<see cref="FailureBehavior.RateLimited"/>) or HTTP 500 (<see cref="FailureBehavior.ServerError"/>).
/// </summary>
public sealed class SandboxProviderFaultException : Exception
{
    public SandboxProviderFaultException(string providerCode, FailureApplication application)
        : base($"Sandbox provider '{providerCode}' returned a simulated {application} fault.")
    {
        ProviderCode = providerCode;
        Application = application;
    }

    public string ProviderCode { get; }
    public FailureApplication Application { get; }
}

/// <summary>
/// Shared enforcement boundary for sandbox failure rules (Req 3). Both the
/// <see cref="FailureSimulatingGovernmentDataProvider"/> decorator (canonical pipeline path) and the
/// direct <c>/sandbox/{provider}/*</c> HTTP endpoints consult this single point so reads honor rules
/// on every path.
/// </summary>
public sealed class SandboxFailureSimulator
{
    private readonly TaxNetState _state;

    public SandboxFailureSimulator(TaxNetState state) => _state = state;

    /// <summary>
    /// Resolves the active decision for the provider and applies the injected latency
    /// (<c>Task.Delay</c> &gt;= InjectedLatencyMs, AC 12), then returns the decision unchanged.
    /// </summary>
    public async Task<FailureDecision> ApplyAsync(string providerCode, CancellationToken ct = default)
    {
        var decision = _state.ResolveFailureDecision(providerCode);
        if (decision.InjectedLatencyMs > 0)
        {
            await Task.Delay(decision.InjectedLatencyMs, ct).ConfigureAwait(false);
        }

        return decision;
    }

    /// <summary>
    /// Transforms a normal record set per the resolved behavior:
    /// <list type="bullet">
    /// <item><see cref="FailureApplication.Offline"/> → throws <see cref="SandboxOfflineException"/> (AC 7).</item>
    /// <item><see cref="FailureApplication.StaleData"/> → ages every record's as-of timestamp strictly before now (AC 10).</item>
    /// <item><see cref="FailureApplication.PartialData"/> → returns a strict, smaller subset (Count-1, min 0) (AC 11).</item>
    /// <item>Everything else (<see cref="FailureApplication.None"/>) → returns the records unchanged (AC 14).</item>
    /// </list>
    /// <see cref="FailureApplication.RateLimited"/>/<see cref="FailureApplication.ServerError"/> are mapped by callers
    /// (HTTP 429/500, AC 8/9) and never reach record shaping.
    /// </summary>
    public IReadOnlyList<T> ShapeRecords<T>(FailureDecision decision, IReadOnlyList<T> normal, Func<T, T> ageTimestamp)
    {
        ArgumentNullException.ThrowIfNull(normal);
        ArgumentNullException.ThrowIfNull(ageTimestamp);

        switch (decision.Application)
        {
            case FailureApplication.Offline:
                throw new SandboxOfflineException(decision.RuleId ?? "sandbox");

            case FailureApplication.StaleData:
                // Age every record's as-of timestamp to strictly before the read time.
                return normal.Select(ageTimestamp).ToArray();

            case FailureApplication.PartialData:
                // Return a strict, smaller subset: Count-1 (min 0).
                var keep = Math.Max(0, normal.Count - 1);
                return normal.Take(keep).ToArray();

            default:
                // None / RateLimited / ServerError leave the records untouched here.
                return normal;
        }
    }
}

/// <summary>
/// <see cref="IGovernmentDataProvider"/> decorator that enforces sandbox failure rules on the canonical
/// pipeline path. It awaits the simulator before delegating to the inner provider, throws
/// <see cref="SandboxOfflineException"/> for Offline, raises <see cref="SandboxProviderFaultException"/> for
/// RateLimited/ServerError, and shapes returned lists for StaleData/PartialData.
/// </summary>
public sealed class FailureSimulatingGovernmentDataProvider : IGovernmentDataProvider
{
    private readonly IGovernmentDataProvider _inner;
    private readonly SandboxFailureSimulator _simulator;

    public FailureSimulatingGovernmentDataProvider(IGovernmentDataProvider inner, SandboxFailureSimulator simulator)
    {
        _inner = inner;
        _simulator = simulator;
    }

    public string ProviderCode => _inner.ProviderCode;
    public string ProviderName => _inner.ProviderName;

    public Task<ProviderHealthResult> CheckHealthAsync(CancellationToken ct = default)
        => _inner.CheckHealthAsync(ct);

    public async Task<TaxProfile?> GetTaxProfileAsync(IdentityToken token, CancellationToken ct = default)
    {
        await EnforceAsync(ct).ConfigureAwait(false);
        return await _inner.GetTaxProfileAsync(token, ct).ConfigureAwait(false);
    }

    public async Task<SyntheticPerson?> GetIdentityProfileAsync(IdentityToken token, CancellationToken ct = default)
    {
        await EnforceAsync(ct).ConfigureAwait(false);
        return await _inner.GetIdentityProfileAsync(token, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VehicleRecord>> GetVehicleRecordsAsync(IdentityToken token, CancellationToken ct = default)
    {
        var decision = await EnforceAsync(ct).ConfigureAwait(false);
        var normal = await _inner.GetVehicleRecordsAsync(token, ct).ConfigureAwait(false);
        return _simulator.ShapeRecords(decision, normal, AgeVehicle);
    }

    public async Task<IReadOnlyList<PropertyRecord>> GetPropertyRecordsAsync(IdentityToken token, CancellationToken ct = default)
    {
        var decision = await EnforceAsync(ct).ConfigureAwait(false);
        var normal = await _inner.GetPropertyRecordsAsync(token, ct).ConfigureAwait(false);
        return _simulator.ShapeRecords(decision, normal, AgeProperty);
    }

    public async Task<IReadOnlyList<BusinessRecord>> GetBusinessRecordsAsync(IdentityToken token, CancellationToken ct = default)
    {
        var decision = await EnforceAsync(ct).ConfigureAwait(false);
        var normal = await _inner.GetBusinessRecordsAsync(token, ct).ConfigureAwait(false);
        return _simulator.ShapeRecords(decision, normal, AgeBusiness);
    }

    public async Task<IReadOnlyList<UtilityBillRecord>> GetUtilitySignalsAsync(IdentityToken token, CancellationToken ct = default)
    {
        var decision = await EnforceAsync(ct).ConfigureAwait(false);
        var normal = await _inner.GetUtilitySignalsAsync(token, ct).ConfigureAwait(false);
        return _simulator.ShapeRecords(decision, normal, AgeUtility);
    }

    public async Task<IReadOnlyList<TravelRecord>> GetTravelSignalsAsync(IdentityToken token, CancellationToken ct = default)
    {
        var decision = await EnforceAsync(ct).ConfigureAwait(false);
        var normal = await _inner.GetTravelSignalsAsync(token, ct).ConfigureAwait(false);
        return _simulator.ShapeRecords(decision, normal, AgeTravel);
    }

    // Resolves + applies latency, surfacing terminal faults before any read happens.
    private async Task<FailureDecision> EnforceAsync(CancellationToken ct)
    {
        var decision = await _simulator.ApplyAsync(ProviderCode, ct).ConfigureAwait(false);
        switch (decision.Application)
        {
            case FailureApplication.Offline:
                throw new SandboxOfflineException(ProviderCode);
            case FailureApplication.RateLimited:
            case FailureApplication.ServerError:
                throw new SandboxProviderFaultException(ProviderCode, decision.Application);
            default:
                return decision;
        }
    }

    // Strictly-before-now timestamp used when aging records for StaleData (AC 10).
    private static DateTimeOffset StaleAsOf() => DateTimeOffset.UtcNow.AddDays(-365);

    private static VehicleRecord AgeVehicle(VehicleRecord r) => r with { SourceUpdatedAtUtc = StaleAsOf() };
    private static PropertyRecord AgeProperty(PropertyRecord r) => r with { SourceUpdatedAtUtc = StaleAsOf() };
    private static BusinessRecord AgeBusiness(BusinessRecord r) => r with { SourceUpdatedAtUtc = StaleAsOf() };
    private static UtilityBillRecord AgeUtility(UtilityBillRecord r) => r with { SourceUpdatedAtUtc = StaleAsOf() };
    private static TravelRecord AgeTravel(TravelRecord r) => r with { SourceUpdatedAtUtc = StaleAsOf() };
}
