using System.Text.Json;
using TaxNetGuardian.Worker.Shared;

var options = WorkerOptions.FromEnvironment("RagPolicy.Worker", "taxnet-dev-rag-index-jobs", args);
return await WorkerHost.RunAsync(options, new RagPolicyWorker());

internal sealed class RagPolicyWorker : IWorkerJobHandler
{
    public async Task HandleAsync(QueueEnvelope envelope, WorkerContext context, CancellationToken cancellationToken)
    {
        await context.Objects.PutObjectAsync(
            "taxnet-dev-raw-source-snapshots",
            $"rag-source/{envelope.CorrelationId}/{envelope.Id}.json",
            "application/json",
            envelope.PayloadJson,
            cancellationToken);

        using var payload = JsonDocument.Parse(envelope.PayloadJson);
        var response = await context.PostApiJsonAsync("/api/system/rag/documents", payload.RootElement, cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
