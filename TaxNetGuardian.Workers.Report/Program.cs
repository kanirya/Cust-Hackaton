using System.Text.Json;
using TaxNetGuardian.Worker.Shared;

var options = WorkerOptions.FromEnvironment("Report.Worker", "taxnet-dev-report-jobs", args);
return await WorkerHost.RunAsync(options, new ReportWorker());

internal sealed class ReportWorker : IWorkerJobHandler
{
    public async Task HandleAsync(QueueEnvelope envelope, WorkerContext context, CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(envelope.PayloadJson);
        var caseId = payload.RootElement.TryGetProperty("caseId", out var caseIdValue) ? caseIdValue.GetString() : null;
        if (string.IsNullOrWhiteSpace(caseId))
        {
            throw new InvalidOperationException("ReportRequested payload requires caseId.");
        }

        var response = await context.PostApiJsonAsync($"/api/reports/cases/{Uri.EscapeDataString(caseId)}", new { requestedBy = context.Options.WorkerName }, cancellationToken);
        response.EnsureSuccessStatusCode();
        var reportJson = await response.Content.ReadAsStringAsync(cancellationToken);
        await context.Objects.PutObjectAsync(
            "taxnet-dev-audit-reports",
            $"worker-generated/{caseId}/{envelope.Id}.json",
            "application/json",
            reportJson,
            cancellationToken);
    }
}
