using TaxNetGuardian.Worker.Shared;

var options = WorkerOptions.FromEnvironment("RiskScoring.Worker", "taxnet-dev-risk-score-jobs", args);
return await WorkerHost.RunAsync(options, new RiskScoringWorker());

internal sealed class RiskScoringWorker : IWorkerJobHandler
{
    public async Task HandleAsync(QueueEnvelope envelope, WorkerContext context, CancellationToken cancellationToken)
    {
        var response = await context.PostApiJsonAsync("/api/system/workers/run", new { reason = "Risk scoring worker cycle", envelope.Id }, cancellationToken);
        response.EnsureSuccessStatusCode();
        await context.Objects.PutObjectAsync(
            "taxnet-dev-worker-artifacts",
            $"risk-scoring/{envelope.Id}.json",
            "application/json",
            await response.Content.ReadAsStringAsync(cancellationToken),
            cancellationToken);
    }
}
