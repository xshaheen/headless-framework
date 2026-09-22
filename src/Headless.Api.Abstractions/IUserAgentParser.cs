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
    /// <summary>Parses everything identifiable out of a User-Agent string.</summary>
    /// <param name="userAgent">The raw <c>User-Agent</c> header value.</param>
    /// <returns>
    /// The parsed result, or <see langword="null"/> when <paramref name="userAgent"/> is blank or nothing could be
    /// identified from it.
    /// </returns>
    /// <remarks>
    /// A User-Agent is self-reported and trivially forged. Use the result for diagnostics, session display, and
    /// analytics; never as an authorization or security input.
    /// </remarks>
    UserAgentInfo? Parse(string? userAgent);

    /// <summary>Parses the operating system and client name from a User-Agent string.</summary>
    /// <param name="userAgent">The raw <c>User-Agent</c> header value.</param>
    /// <returns>
    /// A human-readable string combining OS name and client name (e.g. <c>"Windows Chrome"</c>), or
    /// <see langword="null"/> when <paramref name="userAgent"/> is blank or the device cannot be identified.
    /// </returns>
    /// <remarks>
    /// A display-only shorthand for <see cref="UserAgentInfo.Summary"/>. Call <see cref="Parse"/> instead when any
    /// individual field is wanted, so the fields are not re-derived by string-splitting this.
    /// </remarks>
    string? GetDeviceInfo(string? userAgent) => Parse(userAgent)?.Summary;
}
