// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns;

/// <summary>
/// Manages the iOS 18 broadcast channels of the instance's app: create a channel for an event, share its id
/// with the devices that should follow it, send updates with
/// <see cref="IApnsPushNotificationService.SendBroadcastAsync"/>, and delete it when the event is over.
/// </summary>
/// <remarks>
/// <para>
/// Calls APNs' channel-management endpoint (<c>api-manage-broadcast.push.apple.com:2196</c> in production,
/// <c>api-manage-broadcast.sandbox.push.apple.com:2195</c> in the sandbox) with the instance's credentials. An
/// app can hold up to 10,000 channels per environment, and a channel created in one environment cannot be used
/// in the other.
/// </para>
/// <para>
/// A rejection throws <see cref="ApnsRequestException"/> with APNs' status and reason; a transport fault throws
/// the underlying exception. Unlike a push send, channel management is a control-plane call, so a failure has
/// no partial result to keep.
/// </para>
/// </remarks>
[PublicAPI]
public interface IApnsBroadcastChannelService
{
    /// <summary>Creates a channel and returns its id, a base64 string APNs generates.</summary>
    /// <param name="storagePolicy">
    /// Whether APNs keeps the most recent message for offline devices. Fixed for the channel's life.
    /// </param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <exception cref="ApnsRequestException">APNs rejected the request.</exception>
    ValueTask<ApnsBroadcastChannel> CreateAsync(
        ApnsChannelStoragePolicy storagePolicy,
        CancellationToken cancellationToken = default
    );

    /// <summary>Reads a channel's configuration.</summary>
    /// <param name="channelId">The channel id <see cref="CreateAsync"/> returned.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentException"><paramref name="channelId"/> is empty or white space.</exception>
    /// <exception cref="ApnsRequestException">APNs rejected the request, for example for an unknown channel.</exception>
    ValueTask<ApnsBroadcastChannel> GetAsync(string channelId, CancellationToken cancellationToken = default);

    /// <summary>Lists the ids of every active channel of the app in the instance's environment.</summary>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <exception cref="ApnsRequestException">APNs rejected the request.</exception>
    ValueTask<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a channel. Irreversible: the id is never issued again. APNs may still deliver messages it
    /// already stored for the channel.
    /// </summary>
    /// <param name="channelId">The channel id to delete.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <exception cref="ArgumentException"><paramref name="channelId"/> is empty or white space.</exception>
    /// <exception cref="ApnsRequestException">APNs rejected the request.</exception>
    ValueTask DeleteAsync(string channelId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Specifies whether a broadcast channel keeps a message for devices that are offline when it is sent.
/// </summary>
[PublicAPI]
public enum ApnsChannelStoragePolicy
{
    /// <summary>
    /// APNs stores nothing and delivers each message once. Allows a higher publishing budget, for frequent
    /// updates such as live scores. A broadcast on such a channel must not set a nonzero expiration.
    /// </summary>
    NoMessageStored = 0,

    /// <summary>
    /// APNs keeps the most recent message for up to 8 hours for devices that are offline, for infrequent
    /// updates such as flight status.
    /// </summary>
    MostRecentMessageStored = 1,
}

/// <summary>A broadcast channel of the app.</summary>
/// <param name="Id">The channel id, a base64 string APNs generated. Its length is not fixed.</param>
/// <param name="StoragePolicy">The storage policy fixed when the channel was created.</param>
[PublicAPI]
public sealed record ApnsBroadcastChannel(string Id, ApnsChannelStoragePolicy StoragePolicy);

/// <summary>The exception that is thrown when APNs rejects a channel-management request.</summary>
[PublicAPI]
public sealed class ApnsRequestException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="ApnsRequestException"/> class.</summary>
    public ApnsRequestException() { }

    /// <summary>Initializes a new instance of the <see cref="ApnsRequestException"/> class with a message.</summary>
    /// <param name="message">The message describing the error.</param>
    public ApnsRequestException(string message)
        : base(message) { }

    /// <summary>Initializes a new instance of the <see cref="ApnsRequestException"/> class with a message and inner exception.</summary>
    /// <param name="message">The message describing the error.</param>
    /// <param name="innerException">The inner exception that caused this error.</param>
    public ApnsRequestException(string message, Exception innerException)
        : base(message, innerException) { }

    /// <summary>Initializes a new instance of the <see cref="ApnsRequestException"/> class with response details.</summary>
    /// <param name="message">The message describing the error.</param>
    /// <param name="statusCode">The HTTP status code returned by APNs.</param>
    /// <param name="reason">The error reason returned by APNs.</param>
    /// <param name="requestId">The request identifier from APNs headers.</param>
    public ApnsRequestException(string message, System.Net.HttpStatusCode statusCode, string? reason, string? requestId)
        : base(message)
    {
        StatusCode = statusCode;
        Reason = reason;
        RequestId = requestId;
    }

    /// <summary>Gets the HTTP status code returned by APNs.</summary>
    public System.Net.HttpStatusCode? StatusCode { get; }

    /// <summary>Gets the APNs error reason code, or <see langword="null"/> when not provided.</summary>
    public string? Reason { get; }

    /// <summary>Gets the <c>apns-request-id</c> header value from the rejected request.</summary>
    public string? RequestId { get; }
}
