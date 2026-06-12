namespace TaxNetGuardian.Api;

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
    string ModelVersion);

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

public sealed record AuditExplanation(
    string CaseId,
    string Summary,
    IReadOnlyList<string> KeyReasons,
    IReadOnlyList<string> EvidenceIds,
    IReadOnlyList<PolicyCitation> Citations,
    string HumanReviewWarning);

public sealed record CitizenCorrectionRequest(
    string CaseId,
    string CorrectionType,
    string Message,
    IReadOnlyList<string> EvidenceFileIds);

public sealed record CitizenCorrection(
    string Id,
    string CaseId,
    string CorrectionType,
    string Message,
    IReadOnlyList<string> EvidenceFileIds,
    string Status,
    DateTimeOffset SubmittedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record AssistantRequest(string Question);

public sealed record CnicInvestigationRequest(
    string Cnic,
    bool AllowExternalProvider,
    string? PreferredProvider);

public sealed record CnicInvestigationRecord(
    string Provider,
    string RecordType,
    string RecordId,
    string DisplayName,
    string Summary,
    decimal? Amount,
    DateTimeOffset SourceUpdatedAtUtc);

public sealed record CnicInvestigationSignal(
    string Name,
    string Severity,
    string Detail,
    IReadOnlyList<string> EvidenceIds);

public sealed record CnicInvestigationResult(
    string InvestigationId,
    string Status,
    string CnicMasked,
    object Subject,
    object? CaseContext,
    IReadOnlyList<CnicInvestigationRecord> MatchedRecords,
    IReadOnlyList<CnicInvestigationSignal> Signals,
    string AiNarrative,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> RecommendedActions,
    object Model,
    string HumanReviewWarning,
    DateTimeOffset CompletedAtUtc);

public sealed record CaseAssignmentRequest(string AssignedTo);

public sealed record CaseDecisionRequest(string Decision, string Notes);

// Pre-model context for a streaming CNIC investigation (records/signals/prompt are built
// without calling the model, so the endpoint can stream the narrative separately).
public sealed record CnicInvestigationContext(
    string CnicMasked,
    object Subject,
    object? CaseContext,
    IReadOnlyList<CnicInvestigationRecord> MatchedRecords,
    IReadOnlyList<CnicInvestigationSignal> Signals,
    string Prompt,
    string FallbackNarrative,
    IReadOnlyList<string> Findings,
    IReadOnlyList<string> RecommendedActions,
    string PersonId,
    string? CaseId,
    string PreferredProvider,
    bool AllowExternalProvider);

public sealed record CaseTimelineEvent(
    string Id,
    string CaseId,
    string EventType,
    string Actor,
    string Summary,
    DateTimeOffset TimestampUtc);

public sealed record GeneratedReport(
    string Id,
    string CaseId,
    string StorageUri,
    string GeneratedBy,
    DateTimeOffset GeneratedAtUtc,
    string Watermark,
    string Summary);

