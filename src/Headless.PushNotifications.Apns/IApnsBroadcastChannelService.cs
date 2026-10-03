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
