using System.Text.Json;
using TaxNetGuardian.Worker.Shared;

var options = WorkerOptions.FromEnvironment("Ingestion.Worker", "taxnet-dev-ingestion-jobs", args);
return await WorkerHost.RunAsync(options, new IngestionWorker());

internal sealed class IngestionWorker : IWorkerJobHandler
{
    public async Task HandleAsync(QueueEnvelope envelope, WorkerContext context, CancellationToken cancellationToken)
    {
        await context.Objects.PutObjectAsync(
            "taxnet-dev-raw-source-snapshots",
            $"ingestion/{envelope.CorrelationId}/{envelope.Id}.json",
            "application/json",
            envelope.PayloadJson,
            cancellationToken);

        if (envelope.Type.Equals("DatasetFeedRequested", StringComparison.OrdinalIgnoreCase))
        {
            using var payload = JsonDocument.Parse(envelope.PayloadJson);
            var response = await context.PostApiJsonAsync("/api/sandbox/datasets/feed", payload.RootElement, cancellationToken);
            response.EnsureSuccessStatusCode();
            return;
        }

        if (envelope.Type.Equals("RunIngestionPipeline", StringComparison.OrdinalIgnoreCase))
        {
            var response = await context.PostApiJsonAsync("/api/system/workers/run", new { requestedBy = context.Options.WorkerName }, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }
}
