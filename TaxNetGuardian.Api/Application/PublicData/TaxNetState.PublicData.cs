using System.Security.Cryptography;

namespace TaxNetGuardian.Api;

public sealed partial class TaxNetState
{
    /// <summary>
    /// Approved-public-source policy (Req 1 AC 5, 6). A pure, testable allowlist/denylist over the
    /// fetch source URL. Public notices, fee schedules, policy PDFs, and government press releases on
    /// public government hosts are approved. Anything implying authentication, CAPTCHA, citizen
    /// verification, or terms/privacy-restricted access is rejected.
    /// </summary>
    public static class PublicSourcePolicy
    {
        // Substrings that imply a private / gated / restricted source. These take precedence over the
        // allowlist so a "gov" host with a login/verify path is still rejected.
        private static readonly string[] DisallowedPatterns =
        [
            "login", "signin", "sign-in", "logon", "auth", "oauth", "sso",
            "captcha", "recaptcha",
            "verify", "verification", "citizen-verification", "verify-citizen",
            "account", "myaccount", "dashboard", "portal/login",
            "terms", "privacy", "consent", "register", "registration",
            "password", "session", "token="
        ];

        // Host/url markers that identify approved public government / regulator sources.
        private static readonly string[] AllowedPatterns =
        [
            "gov", "fbr", "nadra", "secp", "press-release", "press_release", "pressrelease",
            "notice", "fee-schedule", "fee_schedule", "feeschedule", "policy", "circular", "gazette"
        ];

        public static bool IsApproved(string sourceUrl, out string reason)
        {
            if (string.IsNullOrWhiteSpace(sourceUrl))
            {
                reason = "Source URL is empty.";
                return false;
            }

            if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                reason = "Source URL is not a valid absolute http(s) URL.";
                return false;
            }

            var normalized = sourceUrl.ToLowerInvariant();
            var host = uri.Host.ToLowerInvariant();

            var matchedDisallowed = DisallowedPatterns.FirstOrDefault(pattern => normalized.Contains(pattern, StringComparison.Ordinal));
            if (matchedDisallowed is not null)
            {
                reason = $"Source rejected: requires authentication or is access-restricted (matched '{matchedDisallowed}').";
                return false;
            }

            var isPublicGovHost = host.EndsWith(".gov.pk", StringComparison.Ordinal) || host.EndsWith(".gov", StringComparison.Ordinal);
            if (isPublicGovHost || AllowedPatterns.Any(pattern => normalized.Contains(pattern, StringComparison.Ordinal)))
            {
                reason = "Approved public government / regulator source.";
                return true;
            }

            reason = "Source is not on the approved public-source allowlist.";
            return false;
        }
    }

    /// <summary>
    /// Deterministic, content-defined SHA-256 content hash as lowercase hex (Req 1 AC 7). Identical
    /// raw bytes always produce an identical hash; any change in bytes changes the hash.
    /// </summary>
    public static string ComputeContentHash(ReadOnlySpan<byte> rawBytes)
    {
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(rawBytes, digest);
        return Convert.ToHexStringLower(digest);
    }

    /// <summary>
    /// Validates approval, records provenance, and submits extracted text to RAG (Req 1 AC 3-10).
    /// Called by <c>POST /api/connectors/public-data/fetch</c> after the worker has fetched and stored
    /// the raw snapshot. Validates before mutating; emits a <c>PublicDataFetch</c> audit event for every
    /// completed fetch regardless of outcome.
    /// </summary>
    public PublicDataFetchResult IngestPublicData(PublicDataIngestRequest request, string actor)
    {
        lock (_lock)
        {
            var completedAtUtc = DateTimeOffset.UtcNow;

            // AC 5, 6: not an approved public source → reject without fetching/indexing.
            if (!PublicSourcePolicy.IsApproved(request.SourceUrl, out var reason))
            {
                AddAuditEvent(actor, "taxnet-data-engineer", "PublicDataFetch", request.SourceUrl, "Rejected", new Dictionary<string, object>
                {
                    ["reason"] = reason,
                    ["sourceType"] = request.SourceType ?? ""
                });
                SaveSnapshot();
                return new PublicDataFetchResult(request.SourceUrl, PublicSourceOutcome.Rejected, request.ContentHash, reason, null, completedAtUtc);
            }

            // AC 10: extraction yielded no non-whitespace text → skip RAG, retain snapshot, record failed-extraction.
            if (string.IsNullOrWhiteSpace(request.ExtractedText))
            {
                const string extractionReason = "Text extraction yielded no non-whitespace content.";
                AddAuditEvent(actor, "taxnet-data-engineer", "PublicDataFetch", request.SourceUrl, "FailedExtraction", new Dictionary<string, object>
                {
                    ["contentHash"] = request.ContentHash ?? "",
                    ["sourceType"] = request.SourceType ?? "",
                    ["rawSnapshotKey"] = request.RawSnapshotKey ?? "",
                    ["reason"] = extractionReason
                });
                SaveSnapshot();
                return new PublicDataFetchResult(request.SourceUrl, PublicSourceOutcome.FailedExtraction, request.ContentHash, extractionReason, null, completedAtUtc);
            }

            // AC 4, 8: approved + non-empty text → submit to RAG via the existing indexing path.
            var title = string.IsNullOrWhiteSpace(request.Title) ? request.SourceUrl : request.Title;
            var sourceType = string.IsNullOrWhiteSpace(request.SourceType) ? "PublicDocument" : request.SourceType;
            var tags = (request.Tags is { Count: > 0 } ? request.Tags : new[] { "public-data", sourceType }).ToArray();

            FeedRagDocument(new RagFeedRequest(title, sourceType, request.SourceUrl, request.ExtractedText, tags));
            var ragDocumentId = RagDocuments.Count > 0 ? RagDocuments[0].Id : null;

            AddAuditEvent(actor, "taxnet-data-engineer", "PublicDataFetch", request.SourceUrl, "Indexed", new Dictionary<string, object>
            {
                ["contentHash"] = request.ContentHash ?? "",
                ["sourceType"] = sourceType,
                ["ragDocumentId"] = ragDocumentId ?? ""
            });
            SaveSnapshot();

            return new PublicDataFetchResult(request.SourceUrl, PublicSourceOutcome.Indexed, request.ContentHash, null, ragDocumentId, completedAtUtc);
        }
    }
}
