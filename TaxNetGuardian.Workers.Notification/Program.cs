using System.Text.Json;
using TaxNetGuardian.Worker.Shared;

var options = WorkerOptions.FromEnvironment("Notification.Worker", "taxnet-dev-notification-jobs", args);
return await WorkerHost.RunAsync(options, new NotificationWorker());

/// <summary>
/// Notification Service worker (Reqs 2, 7). Consumes <c>{ notificationId }</c> jobs and posts to the
/// idempotent API delivery contract. The worker stays thin: resolution, channel routing, the
/// Queued -&gt; Sent transition, and audit are owned in-process by TaxNetState.DeliverNotification.
/// Re-delivery of an already-Sent notification is a no-op 200 from the API.
/// </summary>
internal sealed class NotificationWorker : IWorkerJobHandler
{
    public async Task HandleAsync(QueueEnvelope envelope, WorkerContext context, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(envelope.PayloadJson);
        if (!document.RootElement.TryGetProperty("notificationId", out var idElement) ||
            idElement.GetString() is not { Length: > 0 } notificationId)
        {
            throw new InvalidOperationException("Notification envelope is missing notificationId.");
        }

        using var response = await context.PostApiJsonAsync(
            $"/api/system/notifications/{notificationId}/deliver",
            new { },
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }
}
