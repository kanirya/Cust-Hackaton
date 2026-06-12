using System.Text.Json;
using TaxNetGuardian.Worker.Shared;

var options = WorkerOptions.FromEnvironment("GraphIntelligence.Worker", "taxnet-dev-graph-build-jobs", args);
var log = WorkerLogging.CreateLogger(options.WorkerName);
return await WorkerHost.RunAsync(options, new GraphIntelligenceWorker(log));

internal sealed class GraphIntelligenceWorker : IWorkerJobHandler
{
    private readonly Serilog.ILogger _log;
    public GraphIntelligenceWorker(Serilog.ILogger log) => _log = log;

    public async Task HandleAsync(QueueEnvelope envelope, WorkerContext context, CancellationToken cancellationToken)
    {
        _log.Information(
            "GraphIntelligence job started. Type={Type} Id={Id} CorrelationId={CorrelationId}",
            envelope.Type, envelope.Id, envelope.CorrelationId);

        // 1. Parse entityId from payload
        string entityId;
        try
        {
            using var payload = JsonDocument.Parse(envelope.PayloadJson);
            entityId = payload.RootElement.TryGetProperty("entityId", out var ev)
                ? (ev.GetString() ?? "entity-P001")
                : "entity-P001";
        }
        catch (JsonException ex)
        {
            _log.Warning(ex, "Could not parse payload for entityId — using fallback.");
            entityId = "entity-P001";
        }

        _log.Information("Building graph features for EntityId={EntityId}", entityId);

        // 2. Build graph neighborhood
        var neighborhoodResponse = await context.GetApiAsync(
            $"/api/graph/entities/{Uri.EscapeDataString(entityId)}/neighborhood",
            cancellationToken);

        if (neighborhoodResponse.IsSuccessStatusCode)
        {
            var neighborhoodJson = await neighborhoodResponse.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                using var doc = JsonDocument.Parse(neighborhoodJson);
                var nodeCount = doc.RootElement.TryGetProperty("nodes", out var nodes) ? nodes.GetArrayLength() : 0;
                var edgeCount = doc.RootElement.TryGetProperty("edges", out var edges) ? edges.GetArrayLength() : 0;
                _log.Information("Graph neighborhood built. EntityId={EntityId} Nodes={Nodes} Edges={Edges}",
                    entityId, nodeCount, edgeCount);
            }
            catch (JsonException) { /* non-critical parsing */ }
        }

        // 3. Extract graph features
        var featuresResponse = await context.GetApiAsync(
            $"/api/graph/entities/{Uri.EscapeDataString(entityId)}/features",
            cancellationToken);

        if (!featuresResponse.IsSuccessStatusCode)
        {
            _log.Error("Graph features API failed. EntityId={EntityId} Status={Status}",
                entityId, (int)featuresResponse.StatusCode);
            throw new InvalidOperationException($"Graph features failed for {entityId}: {featuresResponse.StatusCode}");
        }

        var featuresJson = await featuresResponse.Content.ReadAsStringAsync(cancellationToken);

        // 4. Log key graph features
        try
        {
            using var doc = JsonDocument.Parse(featuresJson);
            var assetValue     = doc.RootElement.TryGetProperty("assetValue",     out var av) ? av.GetDecimal() : 0m;
            var luxuryCount    = doc.RootElement.TryGetProperty("luxuryVehicleCount", out var lv) ? lv.GetInt32() : 0;
            var businessCount  = doc.RootElement.TryGetProperty("activeBusinessCount", out var bc) ? bc.GetInt32() : 0;
            var centrality     = doc.RootElement.TryGetProperty("centrality",     out var c)  ? c.GetDecimal() : 0m;

            _log.Information(
                "Graph features extracted. EntityId={EntityId} AssetValue={AssetValue} LuxuryVehicles={Luxury} ActiveBusinesses={Businesses} Centrality={Centrality}",
                entityId, assetValue, luxuryCount, businessCount, centrality);
        }
        catch (JsonException ex)
        {
            _log.Warning(ex, "Could not parse graph feature metrics.");
        }

        // 5. Persist artifact
        var artifactKey = $"graph-features/{entityId}/{DateTimeOffset.UtcNow:yyyy/MM/dd}/{envelope.Id}.json";
        await context.Objects.PutObjectAsync(
            "taxnet-dev-worker-artifacts",
            artifactKey,
            "application/json",
            featuresJson,
            cancellationToken);

        _log.Information("Graph features artifact persisted. EntityId={EntityId} Key={Key}", entityId, artifactKey);
    }
}
