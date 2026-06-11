using System.Text.Json;
using TaxNetGuardian.Worker.Shared;

var options = WorkerOptions.FromEnvironment("GraphIntelligence.Worker", "taxnet-dev-graph-build-jobs", args);
return await WorkerHost.RunAsync(options, new GraphIntelligenceWorker());

internal sealed class GraphIntelligenceWorker : IWorkerJobHandler
{
    public async Task HandleAsync(QueueEnvelope envelope, WorkerContext context, CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(envelope.PayloadJson);
        var entityId = payload.RootElement.TryGetProperty("entityId", out var entityValue) ? entityValue.GetString() : null;
        entityId ??= "entity-P001";
        var response = await context.GetApiAsync($"/api/graph/entities/{Uri.EscapeDataString(entityId)}/features", cancellationToken);
        response.EnsureSuccessStatusCode();
        await context.Objects.PutObjectAsync(
            "taxnet-dev-worker-artifacts",
            $"graph-features/{entityId}/{envelope.Id}.json",
            "application/json",
            await response.Content.ReadAsStringAsync(cancellationToken),
            cancellationToken);
    }
}
