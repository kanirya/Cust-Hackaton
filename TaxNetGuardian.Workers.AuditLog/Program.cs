using TaxNetGuardian.Worker.Shared;

var options = WorkerOptions.FromEnvironment("AuditLog.Worker", "taxnet-dev-audit-log-jobs", args);
return await WorkerHost.RunAsync(options, new AuditLogWorker());

internal sealed class AuditLogWorker : IWorkerJobHandler
{
    public async Task HandleAsync(QueueEnvelope envelope, WorkerContext context, CancellationToken cancellationToken)
    {
        await context.Objects.PutObjectAsync(
            "taxnet-dev-audit-events",
            $"events/{DateTimeOffset.UtcNow:yyyy/MM/dd}/{envelope.Id}.json",
            "application/json",
            envelope.PayloadJson,
            cancellationToken);
    }
}
