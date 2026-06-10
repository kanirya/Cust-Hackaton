namespace TaxNetGuardian.Api;

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

public sealed record RagChunk(
    string Id,
    string DocumentId,
    string Title,
    string Text,
    IReadOnlyList<string> Keywords,
    PolicyCitation Citation,
    decimal QualityScore,
    DateTimeOffset IndexedAtUtc);

public sealed record RagQueryRequest(
    string Query,
    string TaskType,
    string Jurisdiction,
    int TopK,
    IReadOnlyList<string> Tags);

public sealed record RagQueryResult(
    string Query,
    string RewrittenQuery,
    IReadOnlyList<RagChunk> Chunks,
    IReadOnlyList<PolicyCitation> Citations,
    IReadOnlyList<string> QualityChecks,
    decimal RetrievalConfidence);

public sealed record ModelInvocationRequest(
    string TaskType,
    string Prompt,
    string CaseId,
    string PreferredProvider,
    bool AllowExternalProvider);

public sealed record ModelInvocationResponse(
    string InvocationId,
    string TaskType,
    string SelectedProvider,
    string Route,
    string Output,
    IReadOnlyList<string> GuardrailsApplied,
    IReadOnlyList<PolicyCitation> Citations,
    int PromptTokens,
    int CompletionTokens,
    decimal EstimatedCostUsd,
    DateTimeOffset InvokedAtUtc);

public sealed record RagFeedRequest(
    string Title,
    string SourceType,
    string Url,
    string Content,
    IReadOnlyList<string> Tags);

