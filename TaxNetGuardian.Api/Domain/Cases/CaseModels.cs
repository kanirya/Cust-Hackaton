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

public sealed record CaseAssignmentRequest(string AssignedTo);

public sealed record CaseDecisionRequest(string Decision, string Notes);

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

