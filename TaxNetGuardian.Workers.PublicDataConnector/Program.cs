using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TaxNetGuardian.Worker.Shared;

var options = WorkerOptions.FromEnvironment("PublicDataConnector.Worker", "taxnet-dev-public-data-connector-jobs", args);
return await WorkerHost.RunAsync(options, new PublicDataConnectorWorker());

/// <summary>
/// Public Data Connector worker (Reqs 1, 7). Consumes fetch-request envelopes, fetches the document
/// under a 30s timeout with a 50 MB cap, stores the raw snapshot to the raw-source-snapshots bucket
/// with provenance, extracts text, and posts it to the API ingest contract. The worker stays thin:
/// approval classification, content hashing of record, provenance, and audit are owned by the API.
/// </summary>
internal sealed class PublicDataConnectorWorker : IWorkerJobHandler
{
    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(30);   // Req 1 AC 9
    private const long MaxSnapshotBytes = 50L * 1024 * 1024;                    // Req 1 AC 9
    private const string ParserVersion = "public-data-parser-v1.0";
    private const string RawBucket = "taxnet-dev-raw-source-snapshots";
    private const string FetchPath = "/api/connectors/public-data/fetch";

    public async Task HandleAsync(QueueEnvelope envelope, WorkerContext context, CancellationToken cancellationToken)
    {
        var request = JsonSerializer.Deserialize<PublicDataFetchEnvelope>(envelope.PayloadJson, context.JsonOptions)
            ?? throw new InvalidOperationException("Fetch envelope payload could not be parsed.");

        if (string.IsNullOrWhiteSpace(request.SourceUrl))
        {
            throw new InvalidOperationException("Fetch envelope is missing sourceUrl.");
        }

        // The fetch is the only network IO; cap it at 30s independent of the worker cancellation token.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(FetchTimeout);

        byte[] raw;
        try
        {
            using var response = await context.Http.GetAsync(request.SourceUrl, linkedCts.Token);
            response.EnsureSuccessStatusCode();
            raw = await response.Content.ReadAsByteArrayAsync(linkedCts.Token);
            if (raw.LongLength > MaxSnapshotBytes)
            {
                throw new InvalidOperationException($"Snapshot exceeds the {MaxSnapshotBytes}-byte cap.");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || linkedCts.IsCancellationRequested)
        {
            // Network error, 30s timeout, non-success status, or oversize (Req 1 AC 9). Do NOT rethrow
            // for ordinary fetch faults so the worker keeps processing the cycle; post a failed ingest
            // (empty ExtractedText) so the API records a non-indexed outcome with the source URL.
            await PostIngestAsync(context, request, extractedText: "", contentHash: "", rawSizeBytes: 0,
                rawSnapshotKey: "", capturedAtUtc: DateTimeOffset.UtcNow, cancellationToken);
            return;
        }

        var hash = Convert.ToHexStringLower(SHA256.HashData(raw));               // Req 1 AC 3, 7
        var capturedAtUtc = DateTimeOffset.UtcNow;
        var key = $"public-data/{hash}/{envelope.Id}.bin";
        await context.Objects.PutObjectAsync(RawBucket, key, "application/octet-stream", Convert.ToBase64String(raw), cancellationToken);   // Req 1 AC 2

        var extractedText = ExtractText(raw);
        await PostIngestAsync(context, request, extractedText, hash, raw.LongLength, key, capturedAtUtc, cancellationToken);   // Req 1 AC 4
    }

    private static async Task PostIngestAsync(
        WorkerContext context,
        PublicDataFetchEnvelope request,
        string extractedText,
        string contentHash,
        long rawSizeBytes,
        string rawSnapshotKey,
        DateTimeOffset capturedAtUtc,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            sourceUrl = request.SourceUrl,
            sourceType = string.IsNullOrWhiteSpace(request.SourceType) ? "PublicDocument" : request.SourceType,
            title = request.Title,
            extractedText,
            contentHash,
            parserVersion = ParserVersion,
            rawSizeBytes,
            capturedAtUtc,
            rawSnapshotKey,
            tags = request.Tags
        };

        using var response = await context.PostApiJsonAsync(FetchPath, payload, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    /// <summary>
    /// Minimal demo text extraction: decode UTF-8, strip script/style and HTML tags, collapse
    /// whitespace. Sufficient for text/HTML/plain public documents in the sandbox.
    /// </summary>
    private static string ExtractText(byte[] raw)
    {
        if (raw.Length == 0)
        {
            return "";
        }

        var text = Encoding.UTF8.GetString(raw);
        if (text.Contains('<') && text.Contains('>'))
        {
            text = Regex.Replace(text, "<script.*?</script>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<style.*?</style>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
        }

        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
}

internal sealed record PublicDataFetchEnvelope(
    string SourceUrl,
    string? SourceType,
    string? Title,
    IReadOnlyList<string>? Tags);
