using TaxNetGuardian.Worker.Shared;

var options = WorkerOptions.FromEnvironment("IdentityResolution.Worker", "taxnet-dev-identity-resolution-jobs", args);
return await WorkerHost.RunAsync(options, new IdentityResolutionWorker());

internal sealed class IdentityResolutionWorker : IWorkerJobHandler
{
    public async Task HandleAsync(QueueEnvelope envelope, WorkerContext context, CancellationToken cancellationToken)
    {
        var response = await context.GetApiAsync("/api/identity/evaluation", cancellationToken);
        response.EnsureSuccessStatusCode();
        await context.Objects.PutObjectAsync(
            "taxnet-dev-worker-artifacts",
            $"identity-resolution/{envelope.Id}.json",
            "application/json",
            await response.Content.ReadAsStringAsync(cancellationToken),
            cancellationToken);
    }
}
