// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>Parses client information out of a raw <c>User-Agent</c> header value.</summary>
/// <remarks>
/// Parsing a User-Agent is CPU work over a well-known input, so implementations memoize results. The default
/// implementation uses the host's registered <c>Headless.Caching</c> <c>ICache</c> when one is present and parses
/// directly (no memo) when it is absent — a host that wants User-Agent parses cached registers a cache, exactly as
/// with any other optional cache in the framework. The method is asynchronous for that reason: a memo hit may cross
/// an out-of-process tier.
/// </remarks>
public interface IUserAgentParser
{
    /// <summary>Parses the operating system and browser/client name from a User-Agent string.</summary>
    /// <param name="userAgent">The raw <c>User-Agent</c> header value.</param>
    /// <param name="cancellationToken">Cancels an out-of-process memo read/write, when the cache is remote.</param>
    /// <returns>
    /// A human-readable string combining OS name and client name (e.g. <c>"Windows Chrome"</c>), or
    /// <see langword="null"/> when <paramref name="userAgent"/> is blank or the device cannot be identified.
    /// </returns>
    ValueTask<string?> GetDeviceInfoAsync(string? userAgent, CancellationToken cancellationToken = default);
}
