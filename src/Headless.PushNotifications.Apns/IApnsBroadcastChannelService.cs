// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns;

/// <summary>
/// Manages the iOS 18 broadcast channels of the instance's app: create a channel for an event, share its id with the
/// devices that should follow it, send updates with
/// <see cref="IApnsPushNotificationService.SendBroadcastAsync"/>, and delete it when the event is over.
/// </summary>
/// <remarks>
/// <para>
/// Calls APNs' channel-management endpoint (<c>api-manage-broadcast.push.apple.com:2196</c> in production,
/// <c>api-manage-broadcast.sandbox.push.apple.com:2195</c> in the sandbox) with the instance's credentials. An app can
/// hold up to 10,000 channels per environment, and a channel created in one environment cannot be used in the other.
/// </para>
/// <para>
/// A rejection throws <see cref="ApnsRequestException"/> with APNs' status and reason; a transport fault throws the
/// underlying exception. Unlike a push send, channel management is a control-plane call, so a failure has no partial
/// result to keep.
/// </para>
/// </remarks>
[PublicAPI]
public interface IApnsBroadcastChannelService
{
    /// <summary>Creates a channel and returns its id, a base64 string APNs generates.</summary>
    /// <param name="storagePolicy">
    /// Whether APNs keeps the most recent message for offline devices. Fixed for the channel's life.
    /// </param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="ApnsRequestException">APNs rejected the request.</exception>
    ValueTask<ApnsBroadcastChannel> CreateAsync(
        ApnsChannelStoragePolicy storagePolicy,
        CancellationToken cancellationToken = default
    );

    /// <summary>Reads a channel's configuration.</summary>
    /// <param name="channelId">The channel id <see cref="CreateAsync"/> returned.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="ArgumentException"><paramref name="channelId"/> is blank.</exception>
    /// <exception cref="ApnsRequestException">APNs rejected the request, for example for an unknown channel.</exception>
    ValueTask<ApnsBroadcastChannel> GetAsync(string channelId, CancellationToken cancellationToken = default);

    /// <summary>Lists the ids of every active channel of the app in the instance's environment.</summary>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="ApnsRequestException">APNs rejected the request.</exception>
    ValueTask<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a channel. Irreversible: the id is never issued again. APNs may still deliver messages it already
    /// stored for the channel.
    /// </summary>
    /// <param name="channelId">The channel id to delete.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    /// <exception cref="ArgumentException"><paramref name="channelId"/> is blank.</exception>
    /// <exception cref="ApnsRequestException">APNs rejected the request.</exception>
    ValueTask DeleteAsync(string channelId, CancellationToken cancellationToken = default);
}

/// <summary>Whether a broadcast channel keeps a message for devices that are offline when it is sent.</summary>
[PublicAPI]
public enum ApnsChannelStoragePolicy
{
    /// <summary>
    /// APNs stores nothing and delivers each message once. Allows a higher publishing budget, for frequent updates
    /// such as live scores. A broadcast on such a channel must not set a nonzero expiration.
    /// </summary>
    NoMessageStored = 0,

    /// <summary>
    /// APNs keeps the most recent message for up to 8 hours for devices that are offline, for infrequent updates such
    /// as flight status.
    /// </summary>
    MostRecentMessageStored = 1,
}

/// <summary>A broadcast channel of the app.</summary>
/// <param name="Id">The channel id, a base64 string APNs generated. Its length is not fixed.</param>
/// <param name="StoragePolicy">The storage policy fixed when the channel was created.</param>
[PublicAPI]
public sealed record ApnsBroadcastChannel(string Id, ApnsChannelStoragePolicy StoragePolicy);

/// <summary>APNs rejected a channel-management request.</summary>
[PublicAPI]
public sealed class ApnsRequestException : Exception
{
    /// <summary>Creates the exception.</summary>
    public ApnsRequestException() { }

    /// <summary>Creates the exception with a message.</summary>
    public ApnsRequestException(string message)
        : base(message) { }

    /// <summary>Creates the exception with a message and an inner exception.</summary>
    public ApnsRequestException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>Creates the exception for an APNs answer.</summary>
    public ApnsRequestException(string message, System.Net.HttpStatusCode statusCode, string? reason, string? requestId)
        : base(message)
    {
        StatusCode = statusCode;
        Reason = reason;
        RequestId = requestId;
    }

    /// <summary>The HTTP status APNs answered with.</summary>
    public System.Net.HttpStatusCode? StatusCode { get; }

    /// <summary>The APNs error code from the response body, or <see langword="null"/> when it carried none.</summary>
    public string? Reason { get; }

    /// <summary>The <c>apns-request-id</c> of the rejected request, to quote when troubleshooting with Apple.</summary>
    public string? RequestId { get; }
}
