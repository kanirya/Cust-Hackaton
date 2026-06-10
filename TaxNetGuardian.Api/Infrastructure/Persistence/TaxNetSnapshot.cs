namespace TaxNetGuardian.Api;

public sealed record TaxNetSnapshot
{
    public int Version { get; init; } = 1;
    public DateTimeOffset SavedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public List<SyntheticPerson> People { get; init; } = [];
    public List<TaxProfile> TaxProfiles { get; init; } = [];
    public List<VehicleRecord> Vehicles { get; init; } = [];
    public List<PropertyRecord> Properties { get; init; } = [];
    public List<UtilityBillRecord> UtilityBills { get; init; } = [];
    public List<BusinessRecord> Businesses { get; init; } = [];
    public List<TravelRecord> Travel { get; init; } = [];
    public List<ResolvedEntity> Entities { get; init; } = [];
    public List<CaseItem> Cases { get; init; } = [];
    public List<WorkerStatus> Workers { get; init; } = [];
    public List<ProviderDescriptor> Providers { get; init; } = [];
    public List<RagDocument> RagDocuments { get; init; } = [];
    public List<RagChunk> RagChunks { get; init; } = [];
    public List<DatasetBatch> DatasetBatches { get; init; } = [];
    public List<ImportJob> ImportJobs { get; init; } = [];
    public List<CaseTimelineEvent> TimelineEvents { get; init; } = [];
    public List<GeneratedReport> Reports { get; init; } = [];
    public List<ModelInvocationResponse> ModelInvocations { get; init; } = [];
    public List<AuditEvent> AuditEvents { get; init; } = [];
    public List<NotificationItem> Notifications { get; init; } = [];
    public List<ObjectStoreItem> ObjectStore { get; init; } = [];
    public List<CitizenCorrection> Corrections { get; init; } = [];
    public Dictionary<string, ProviderConfigUpdateRequest> ProviderConfigs { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
