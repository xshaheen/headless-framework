// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.PushNotifications.Apns;

/// <summary>A broadcast channel of the app.</summary>
/// <param name="Id">The channel id, a base64 string APNs generated. Its length is not fixed.</param>
/// <param name="StoragePolicy">The storage policy fixed when the channel was created.</param>
[PublicAPI]
public sealed record ApnsBroadcastChannel(string Id, ApnsChannelStoragePolicy StoragePolicy);
