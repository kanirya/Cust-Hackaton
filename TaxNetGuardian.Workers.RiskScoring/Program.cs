using System.Text.Json;
using TaxNetGuardian.Worker.Shared;

var options = WorkerOptions.FromEnvironment("RiskScoring.Worker", "taxnet-dev-risk-score-jobs", args);
var log = WorkerLogging.CreateLogger(options.WorkerName);
return await WorkerHost.RunAsync(options, new RiskScoringWorker(log));

internal sealed class RiskScoringWorker : IWorkerJobHandler
{
    private readonly Serilog.ILogger _log;
    public RiskScoringWorker(Serilog.ILogger log) => _log = log;

    public async Task HandleAsync(QueueEnvelope envelope, WorkerContext context, CancellationToken cancellationToken)
    {
        _log.Information(
            "RiskScoring job started. Type={Type} Id={Id} CorrelationId={CorrelationId}",
            envelope.Type, envelope.Id, envelope.CorrelationId);

        // Parse optional batchId / entityId from payload
        string? batchId = null;
        string? entityId = null;
        try
        {
            using var payload = JsonDocument.Parse(envelope.PayloadJson);
            batchId  = payload.RootElement.TryGetProperty("batchId",  out var b) ? b.GetString() : null;
            entityId = payload.RootElement.TryGetProperty("entityId", out var e) ? e.GetString() : null;
        }
        catch (JsonException ex)
        {
            _log.Warning(ex, "Could not parse risk scoring payload. Using full cycle.");
        }

        _log.Information("Executing risk scoring cycle. BatchId={BatchId} EntityId={EntityId}", batchId ?? "all", entityId ?? "all");

        // Trigger the risk scoring pipeline
        var response = await context.PostApiJsonAsync(
            "/api/system/workers/run",
            new
            {
                reason        = "RiskScoring.Worker cycle",
                jobId         = envelope.Id,
                correlationId = envelope.CorrelationId,
                batchId,
                entityId,
                requestedBy   = context.Options.WorkerName
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            _log.Error("Risk scoring cycle failed. Status={Status} Body={Body}",
                (int)response.StatusCode, body[..Math.Min(300, body.Length)]);
            throw new InvalidOperationException($"Risk scoring failed: {response.StatusCode}");
        }

        var resultJson = await response.Content.ReadAsStringAsync(cancellationToken);

        // Log key outcome metrics
        try
        {
            using var doc = JsonDocument.Parse(resultJson);
            _log.Information("Risk scoring cycle complete. Preview={Preview}",
                resultJson[..Math.Min(200, resultJson.Length)]);
        }
        catch (JsonException) { /* non-critical */ }

        // Persist scored artifact
        var artifactKey = $"risk-scoring/{DateTimeOffset.UtcNow:yyyy/MM/dd}/{envelope.Id}.json";
        await context.Objects.PutObjectAsync(
            "taxnet-dev-worker-artifacts",
            artifactKey,
            "application/json",
            resultJson,
            cancellationToken);

        _log.Information("Risk scoring artifact persisted. Key={Key}", artifactKey);
    }
}
