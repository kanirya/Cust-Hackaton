namespace TaxNetGuardian.Api;

public sealed record PublicDataFetchRequest(
    string SourceUrl,
    string SourceType,          // e.g. "PublicTaxNotice", "PublicFeeSchedule", "PublicPolicyPdf"
    string? Title,
    IReadOnlyList<string>? Tags);

public sealed record PublicDataIngestRequest(   // worker -> API after it has fetched+stored
    string SourceUrl,
    string SourceType,
    string? Title,
    string ExtractedText,
    string ContentHash,         // SHA-256 hex of raw bytes
    string ParserVersion,       // e.g. "public-data-parser-v1.0"
    long RawSizeBytes,
    DateTimeOffset CapturedAtUtc,
    string RawSnapshotKey,      // S3 key under taxnet-dev-raw-source-snapshots
    IReadOnlyList<string>? Tags);

public enum PublicSourceOutcome { Indexed, Rejected, Failed, FailedExtraction }

public sealed record PublicDataFetchResult(
    string SourceUrl,
    PublicSourceOutcome Outcome,
    string? ContentHash,
    string? FailureReason,
    string? RagDocumentId,
    DateTimeOffset CompletedAtUtc);
