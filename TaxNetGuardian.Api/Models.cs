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

public sealed record ResolvedEntity(
    string Id,
    string PersonId,
    decimal MatchConfidence,
    IReadOnlyList<string> LinkedRecordIds,
    IReadOnlyList<string> MatchReasons,
    bool RequiresHumanReview);

public sealed record RiskScoreComponent(
    string Name,
    int Score,
    int MaxScore,
    IReadOnlyList<string> EvidenceIds,
    string Explanation);

public sealed record RiskScore(
    string CaseId,
    string EntityId,
    int Score,
    string RiskBand,
    decimal Confidence,
    IReadOnlyList<RiskScoreComponent> Components,
    string RecommendedAction,
    string ScoreVersion);

public sealed record EvidenceItem(
    string Id,
    string Type,
    string Title,
    string Description,
    decimal? Amount,
    string Source,
    DateTimeOffset SourceUpdatedAtUtc);

public sealed record CaseItem(
    string Id,
    string EntityId,
    string PersonId,
    string Status,
    string AssignedTo,
    string City,
    string Province,
    RiskScore Score,
    IReadOnlyList<EvidenceItem> Evidence,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record GraphNode(
    string Id,
    string Type,
    string Label,
    string RiskBand,
    IReadOnlyDictionary<string, object> Properties);

public sealed record GraphEdge(
    string Id,
    string Source,
    string Target,
    string Type,
    decimal Confidence,
    IReadOnlyDictionary<string, object> Properties);

public sealed record GraphNeighborhood(IReadOnlyList<GraphNode> Nodes, IReadOnlyList<GraphEdge> Edges);

public sealed record PolicyCitation(
    string Title,
    string Url,
    string SourceType,
    string ChunkId,
    DateTimeOffset CapturedAtUtc);

public sealed record RagDocument(
    string Id,
    string Title,
    string SourceType,
    string Url,
    string Summary,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<string> Tags);

public sealed record AuditExplanation(
    string CaseId,
    string Summary,
    IReadOnlyList<string> KeyReasons,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<PolicyCitation> Citations,
    string HumanReviewWarning);

public sealed record AssistantRequest(string Question);

public sealed record CitizenCorrectionRequest(
    string CaseId,
    string CorrectionType,
    string Message,
    IReadOnlyList<string> EvidenceFileIds);

public sealed record ProviderDescriptor(
    string ProviderCode,
    string Name,
    string Mode,
    string Status,
    int LatencyMs,
    bool SupportsBulkImport,
    string CredentialSecretName,
    string LastHealthStatus);

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

public sealed record SandboxGenerateRequest(int Count, int SuspiciousPercent, int NoisePercent);
