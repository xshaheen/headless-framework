// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;

namespace Headless.IpGeolocation;

/// <summary>Resolves an IP address to its approximate location and network owner.</summary>
/// <remarks>
/// Implementations are thread-safe singletons. A lookup reads a local database, so it is synchronous and does no
/// network I/O.
/// </remarks>
[PublicAPI]
public interface IIpGeolocator
{
    /// <summary>Looks up <paramref name="address" /> in the configured databases.</summary>
    /// <param name="address">An IPv4 or IPv6 address.</param>
    /// <returns>
    /// The location, or <see langword="null" /> when no database knows the address, such as a private, loopback, or
    /// reserved address, or when no database is loaded yet.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="address" /> is <see langword="null" />.</exception>
    IpLocation? Locate(IPAddress address);
}
