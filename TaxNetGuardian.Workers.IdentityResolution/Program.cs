using System.Text.Json;
using TaxNetGuardian.Worker.Shared;

var options = WorkerOptions.FromEnvironment("IdentityResolution.Worker", "taxnet-dev-identity-resolution-jobs", args);
var log = WorkerLogging.CreateLogger(options.WorkerName);
return await WorkerHost.RunAsync(options, new IdentityResolutionWorker(log));

internal sealed class IdentityResolutionWorker : IWorkerJobHandler
{
    private readonly Serilog.ILogger _log;
    public IdentityResolutionWorker(Serilog.ILogger log) => _log = log;

    public async Task HandleAsync(QueueEnvelope envelope, WorkerContext context, CancellationToken cancellationToken)
    {
        _log.Information(
            "IdentityResolution job started. Type={Type} Id={Id} CorrelationId={CorrelationId}",
            envelope.Type, envelope.Id, envelope.CorrelationId);

        // 1. Trigger identity resolution and retrieve evaluation metrics
        var evalResponse = await context.GetApiAsync("/api/identity/evaluation", cancellationToken);
        if (!evalResponse.IsSuccessStatusCode)
        {
            _log.Error("Identity evaluation API failed. Status={Status}", (int)evalResponse.StatusCode);
            throw new InvalidOperationException($"Identity evaluation failed: {evalResponse.StatusCode}");
        }

        var evalJson = await evalResponse.Content.ReadAsStringAsync(cancellationToken);

        // 2. Log key resolution metrics
        try
        {
            using var doc = JsonDocument.Parse(evalJson);
            var resolved = doc.RootElement.TryGetProperty("resolvedEntities", out var re) ? re.GetInt32() : 0;
            var precision = doc.RootElement.TryGetProperty("precision", out var p) ? p.GetDecimal() : 0m;
            var recall    = doc.RootElement.TryGetProperty("recall",    out var r) ? r.GetDecimal() : 0m;
            var ambiguity = doc.RootElement.TryGetProperty("ambiguityRate", out var a) ? a.GetDecimal() : 0m;

            _log.Information(
                "Identity resolution complete. ResolvedEntities={Resolved} Precision={Precision:P0} Recall={Recall:P0} AmbiguityRate={Ambiguity:P0}",
                resolved, precision, recall, ambiguity);
        }
        catch (JsonException ex)
        {
            _log.Warning(ex, "Could not parse identity evaluation metrics from response.");
        }

        // 3. Persist artifact
        var artifactKey = $"identity-resolution/{DateTimeOffset.UtcNow:yyyy/MM/dd}/{envelope.Id}.json";
        await context.Objects.PutObjectAsync(
            "taxnet-dev-worker-artifacts",
            artifactKey,
            "application/json",
            evalJson,
            cancellationToken);

        _log.Information("Identity resolution artifact persisted. Key={Key}", artifactKey);
    }
}
