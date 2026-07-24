// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>Parses client information out of a raw <c>User-Agent</c> header value.</summary>
/// <remarks>
/// Implementations are expected to be registered as singletons and to be safe for concurrent use.
/// Parsing a User-Agent is pure CPU work over a well-known input, so implementations should memoize
/// results in-process rather than in a shared cache — a network round trip to skip a local parse is a
/// net loss, and two nodes parsing the same string always agree.
/// </remarks>
public interface IUserAgentParser
{
    /// <summary>Parses the operating system and browser/client name from a User-Agent string.</summary>
    /// <param name="userAgent">The raw <c>User-Agent</c> header value.</param>
    /// <returns>
    /// A human-readable string combining OS name and client name (e.g. <c>"Windows Chrome"</c>), or
    /// <see langword="null"/> when <paramref name="userAgent"/> is blank or the device cannot be identified.
    /// </returns>
    string? GetDeviceInfo(string? userAgent);
}
