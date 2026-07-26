// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>Parses client information out of a raw <c>User-Agent</c> header value.</summary>
/// <remarks>
/// Parsing a User-Agent is local CPU work over a well-known input, so implementations may memoize results. The
/// default implementation owns a bounded in-process cache so parser entries do not consume the host application's
/// cache budget or cross a process boundary.
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
