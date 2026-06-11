using System.Text.Json;
using TaxNetGuardian.Worker.Shared;

var options = WorkerOptions.FromEnvironment("Ingestion.Worker", "taxnet-dev-ingestion-jobs", args);
using var log = WorkerLogging.CreateLogger(options.WorkerName);
return await WorkerHost.RunAsync(options, new IngestionWorker(log));

internal sealed class IngestionWorker : IWorkerJobHandler
{
    private readonly Serilog.ILogger _log;
    public IngestionWorker(Serilog.ILogger log) => _log = log;

    public async Task HandleAsync(QueueEnvelope envelope, WorkerContext context, CancellationToken cancellationToken)
    {
        _log.Information(
            "Ingestion job started. Type={Type} Id={Id} Attempt={Attempt} CorrelationId={CorrelationId}",
            envelope.Type, envelope.Id, envelope.Attempt, envelope.CorrelationId);

        // 1. Store raw snapshot — idempotent S3/file write
        var snapshotKey = $"ingestion/{envelope.CorrelationId}/{envelope.Id}.json";
        await context.Objects.PutObjectAsync(
            "taxnet-dev-raw-source-snapshots",
            snapshotKey,
            "application/json",
            envelope.PayloadJson,
            cancellationToken);

        _log.Information("Raw snapshot stored. Key={Key}", snapshotKey);

        // 2. Route by job type
        if (envelope.Type.Equals("DatasetFeedRequested", StringComparison.OrdinalIgnoreCase))
        {
            using var payload = JsonDocument.Parse(envelope.PayloadJson);
            _log.Information("Processing DatasetFeedRequested — forwarding to API ingestion pipeline.");
            var response = await context.PostApiJsonAsync("/api/sandbox/datasets/feed", payload.RootElement, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _log.Error("Dataset feed failed. Status={Status} Body={Body}", (int)response.StatusCode, body[..Math.Min(300, body.Length)]);
                throw new InvalidOperationException($"Dataset feed returned {response.StatusCode}: {body}");
            }

            _log.Information("DatasetFeedRequested completed successfully.");
            return;
        }

        if (envelope.Type.Equals("RunIngestionPipeline", StringComparison.OrdinalIgnoreCase))
        {
            _log.Information("Running full ingestion pipeline cycle.");
            var response = await context.PostApiJsonAsync(
                "/api/system/workers/run",
                new { requestedBy = context.Options.WorkerName, correlationId = envelope.CorrelationId },
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _log.Error("Ingestion pipeline run failed. Status={Status}", (int)response.StatusCode);
                throw new InvalidOperationException($"Ingestion pipeline failed: {body}");
            }

            var resultJson = await response.Content.ReadAsStringAsync(cancellationToken);
            _log.Information("Ingestion pipeline completed. Result={Preview}",
                resultJson[..Math.Min(200, resultJson.Length)]);
            return;
        }

        _log.Warning("Unknown ingestion job type. Type={Type} — raw snapshot stored only.", envelope.Type);
    }
}
