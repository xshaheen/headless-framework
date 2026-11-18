// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

/// <summary>
/// The network path between a client and its server (a broker or a database), which a test can cut while the server
/// keeps running. A Toxiproxy route implements it against a real server, and the messaging simulator by dropping its
/// connection on cue.
/// </summary>
[PublicAPI]
public interface INetworkLink
{
    /// <summary>Cuts every open connection and refuses new ones until <see cref="RestoreAsync"/>.</summary>
    Task CutAsync(CancellationToken cancellationToken = default);

    /// <summary>Accepts connections again.</summary>
    Task RestoreAsync(CancellationToken cancellationToken = default);
}
