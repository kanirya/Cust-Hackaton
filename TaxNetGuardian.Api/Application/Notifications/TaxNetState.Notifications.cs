namespace TaxNetGuardian.Api;

public sealed partial class TaxNetState
{
    /// <summary>
    /// Idempotent in-app notification delivery (Req 2 AC 2,3,6,7,8,9,10). Resolves the notification
    /// by id, routes it through the channel registry, and transitions <c>Queued -&gt; Sent</c> exactly
    /// once. The returned outcome is one of <c>Sent</c>, <c>AlreadySent</c>, <c>Failed</c>, or
    /// <c>Rejected</c>. State is only mutated (and snapshotted) when a queued item is delivered.
    /// </summary>
    public (NotificationItem? Item, NotificationDeliveryResult Result, string Outcome) DeliverNotification(
        string notificationId,
        INotificationChannelRegistry channels,
        string actor)
    {
        lock (_lock)
        {
            var index = Notifications.FindIndex(x =>
                x.Id.Equals(notificationId, StringComparison.OrdinalIgnoreCase));

            // AC 10 — unknown id: no state change, audit a rejected outcome.
            if (index < 0)
            {
                var rejected = new NotificationDeliveryResult(false, "None", false, "Notification not found.");
                AddAuditEvent(actor, "taxnet-notification-worker", "NotificationDelivered", notificationId, "Rejected", new Dictionary<string, object>
                {
                    ["recipient"] = "",
                    ["channel"] = "None",
                    ["requestedChannelUnavailable"] = false
                });
                return (null, rejected, "Rejected");
            }

            var item = Notifications[index];

            // AC 8 — already sent: do not re-flip, no audit/state change.
            if (item.Status.Equals("Sent", StringComparison.OrdinalIgnoreCase))
            {
                var already = new NotificationDeliveryResult(true, item.Channel, false, null);
                return (item, already, "AlreadySent");
            }

            // Resolve the channel; fall back to InApp and flag when the requested channel is
            // unconfigured (AC 6). The flag is computed by comparing requested to resolved.
            var channel = channels.Resolve(item.Channel);
            var requestedChannelUnavailable = !channel.Channel.Equals(item.Channel, StringComparison.OrdinalIgnoreCase);

            // InApp delivery is a synchronous no-op; awaiting under the lock cannot deadlock.
            var delivery = channel.DeliverAsync(item, CancellationToken.None).GetAwaiter().GetResult();
            var result = delivery with { RequestedChannelUnavailable = requestedChannelUnavailable };

            // AC 9 — delivery failure: leave Queued, audit the failure, no status change.
            if (!result.Delivered)
            {
                AddAuditEvent(actor, "taxnet-notification-worker", "NotificationDelivered", notificationId, "Failed", new Dictionary<string, object>
                {
                    ["recipient"] = item.Recipient,
                    ["channel"] = result.ChannelUsed,
                    ["requestedChannelUnavailable"] = result.RequestedChannelUnavailable
                });
                return (item, result, "Failed");
            }

            // AC 3,4,6,7 — success: Queued -> Sent exactly once, audit, snapshot.
            var sent = item with { Status = "Sent" };
            Notifications[index] = sent;
            AddAuditEvent(actor, "taxnet-notification-worker", "NotificationDelivered", notificationId, "Sent", new Dictionary<string, object>
            {
                ["recipient"] = sent.Recipient,
                ["channel"] = result.ChannelUsed,
                ["requestedChannelUnavailable"] = result.RequestedChannelUnavailable
            });
            SaveSnapshot();
            return (sent, result, "Sent");
        }
    }

    /// <summary>
    /// Producer-side helper that returns the ids of all notifications currently in <c>Queued</c>
    /// status, without mutating state. Used by <c>RunWorkerCycle</c> and notification-producing code
    /// to enqueue delivery jobs.
    /// </summary>
    public IReadOnlyList<string> EnqueueQueuedNotificationIds()
    {
        lock (_lock)
        {
            return Notifications
                .Where(x => x.Status.Equals("Queued", StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Id)
                .ToArray();
        }
    }
}
