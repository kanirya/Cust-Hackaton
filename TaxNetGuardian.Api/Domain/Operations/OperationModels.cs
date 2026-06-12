namespace TaxNetGuardian.Api;

public sealed record AuditEvent(
    string Id,
    string Actor,
    string ActorRole,
    string Action,
    string Resource,
    string Outcome,
    string CorrelationId,
    DateTimeOffset TimestampUtc,
    IReadOnlyDictionary<string, object> Metadata);

public sealed record NotificationItem(
    string Id,
    string Recipient,
    string Channel,
    string Subject,
    string Body,
    string Status,
    DateTimeOffset CreatedAtUtc);

// Persisted assistant conversation turn (per case), so chat history survives restarts.
public sealed record ChatMessage(
    string Id,
    string CaseId,
    string Role,
    string Text,
    DateTimeOffset CreatedAtUtc);

public sealed record FeatureFlagUpdateRequest(string Key, bool Enabled);

public sealed record FeatureFlagView(string Key, string Label, string Description, string Category, bool Enabled);

// Unified, ranked search result used by the global command search and entity lookup.
public sealed record SearchHit(
    string Kind,
    string MatchType,
    decimal Confidence,
    string? PersonId,
    string? CaseId,
    string FullName,
    string? UrduName,
    string? FatherName,
    string CnicMasked,
    string? Ntn,
    string City,
    string Province,
    string? RiskBand,
    int? Score,
    string Snippet);

public sealed record SearchResponse(string Query, int Count, IReadOnlyList<SearchHit> Items);

public sealed record ObjectStoreItem(
    string Uri,
    string Bucket,
    string Key,
    string ContentType,
    long SizeBytes,
    DateTimeOffset StoredAtUtc);

public sealed record ProviderDescriptor(
    string ProviderCode,
    string Name,
    string Mode,
    string Status,
    int LatencyMs,
    bool SupportsBulkImport,
    string CredentialSecretName,
    string LastHealthStatus);

public sealed record ProviderConfigUpdateRequest(
    string Mode,
    string BaseUrl,
    string CredentialSecretName,
    bool Enabled,
    int RateLimitPerMinute,
    string Notes);

public sealed record WorkerStatus(
    string Name,
    string QueueName,
    int QueueDepth,
    int ProcessedToday,
    int FailedToday,
    string Status,
    DateTimeOffset LastRunAtUtc);

public sealed record DashboardSummary(
    int TotalProfiles,
    int TotalCases,
    int CriticalCases,
    int HighCases,
    int MediumCases,
    decimal EstimatedRecoverableTax,
    decimal EntityResolutionPrecision,
    decimal EntityResolutionRecall,
    IReadOnlyDictionary<string, int> CasesByCity,
    IReadOnlyDictionary<string, int> CasesByReason);

public sealed record AuthzRole(string Role, string Description, IReadOnlyList<string> Scopes, IReadOnlyList<string> UiRoutes);

public sealed record PathAccessPolicy(string PathPrefix, IReadOnlyList<string> AllowedRoles);

public sealed record AccessDecision(bool Allowed, string Role, IReadOnlyList<string> RequiredRoles, string Path);

public sealed record SandboxGenerateRequest(int Count, int SuspiciousPercent, int NoisePercent);

public sealed record DatasetFeedRequest(
    string DatasetType,
    string Format,
    string FileName,
    string Content,
    bool RunPipeline);

public sealed record DatasetBatch(
    string Id,
    string DatasetType,
    string Format,
    string FileName,
    int RecordCount,
    string Status,
    DateTimeOffset UploadedAtUtc,
    IReadOnlyList<string> Warnings);

public sealed record ImportJob(
    string Id,
    string Type,
    string Status,
    string Source,
    int RecordsProcessed,
    int RecordsCreated,
    int RecordsFailed,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? FinishedAtUtc,
    IReadOnlyList<string> Messages);

public sealed record DatasetTemplate(
    string DatasetType,
    string Description,
    IReadOnlyList<string> Columns,
    string CsvExample);

public sealed record WorkerEnqueueRequest(
    string QueueName,
    string Type,
    string PayloadJson,
    string? CorrelationId);

public sealed record QueuedWorkerMessage(string QueueName, TaxNetGuardian.Worker.Shared.QueueEnvelope Envelope);

