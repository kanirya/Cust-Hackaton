namespace TaxNetGuardian.Api;

/// <summary>
/// Outcome of attempting to deliver a <see cref="NotificationItem"/> through a channel (Req 2).
/// <paramref name="RequestedChannelUnavailable"/> is true when the requested channel had no
/// configured implementation and delivery fell back to the in-app channel (Req 2 AC 6).
/// </summary>
public sealed record NotificationDeliveryResult(
    bool Delivered,
    string ChannelUsed,
    bool RequestedChannelUnavailable,
    string? Error);

/// <summary>
/// A pluggable delivery target for a notification (Req 2 AC 5). The only implemented channel is
/// <c>InApp</c>; production channels (SNS, email, SMS) are expressible through the same abstraction
/// without changing notification-producing code.
/// </summary>
public interface INotificationChannel
{
    string Channel { get; }
    Task<NotificationDeliveryResult> DeliverAsync(NotificationItem item, CancellationToken cancellationToken);
}

/// <summary>
/// Resolves the channel for a requested channel name, falling back to the in-app channel when no
/// implementation is configured for the request (Req 2 AC 6).
/// </summary>
public interface INotificationChannelRegistry
{
    INotificationChannel Resolve(string requestedChannel);
}

/// <summary>
/// In-app delivery: a no-op success. "In-app" means the notification is visible via
/// <c>GET /api/system/notifications</c>, so there is no external delivery to perform.
/// </summary>
public sealed class InAppNotificationChannel : INotificationChannel
{
    public string Channel => "InApp";

    public Task<NotificationDeliveryResult> DeliverAsync(NotificationItem item, CancellationToken cancellationToken)
        => Task.FromResult(new NotificationDeliveryResult(
            Delivered: true,
            ChannelUsed: Channel,
            RequestedChannelUnavailable: false,
            Error: null));
}

/// <summary>
/// Registry over the configured channels. Matches the requested channel by name (case-insensitive)
/// and, when none matches, returns the in-app channel so delivery still succeeds (Req 2 AC 6). The
/// "requested channel unavailable" flag is determined by the caller comparing the requested channel
/// to the resolved channel's <see cref="INotificationChannel.Channel"/>.
/// </summary>
public sealed class NotificationChannelRegistry : INotificationChannelRegistry
{
    private readonly INotificationChannel _inApp;
    private readonly IReadOnlyList<INotificationChannel> _channels;

    public NotificationChannelRegistry(IEnumerable<INotificationChannel> channels)
    {
        _channels = channels.ToArray();
        _inApp = _channels.FirstOrDefault(x => x.Channel.Equals("InApp", StringComparison.OrdinalIgnoreCase))
            ?? new InAppNotificationChannel();
    }

    public INotificationChannel Resolve(string requestedChannel)
    {
        if (string.IsNullOrWhiteSpace(requestedChannel))
        {
            return _inApp;
        }

        return _channels.FirstOrDefault(x => x.Channel.Equals(requestedChannel, StringComparison.OrdinalIgnoreCase))
            ?? _inApp;
    }
}
