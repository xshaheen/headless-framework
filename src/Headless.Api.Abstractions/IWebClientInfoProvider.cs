// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>Provides network and client identity information derived from the current HTTP request.</summary>
/// <remarks>
/// All members return <see langword="null"/> when no HTTP request is in scope (e.g. background workers,
/// console hosts, or non-HTTP middleware). Implementations sourced from ASP.NET Core should
/// resolve from <c>IHttpContextAccessor</c> and handle a <see langword="null"/>
/// <c>HttpContext</c> gracefully.
/// </remarks>
public interface IWebClientInfoProvider
{
    /// <summary>Gets the originating client IP address of the current request.</summary>
    /// <remarks>
    /// Returns <see langword="null"/> outside an HTTP scope or when the address cannot be determined.
    /// Implementations behind a reverse proxy should resolve the real client IP from forwarded headers
    /// (e.g. <c>X-Forwarded-For</c>) rather than the immediate connection peer.
    /// </remarks>
    string? IpAddress { get; }

    /// <summary>Gets the <c>User-Agent</c> header value sent by the client.</summary>
    /// <remarks>Returns <see langword="null"/> outside an HTTP scope or when the header is absent.</remarks>
    string? UserAgent { get; }

    /// <summary>Gets device-identifying information parsed or derived from the current request.</summary>
    /// <param name="cancellationToken">Cancels an out-of-process memo read/write, when device info is cached remotely.</param>
    /// <remarks>
    /// Returns <see langword="null"/> outside an HTTP scope or when no device info is available. This member is
    /// asynchronous — unlike <see cref="IpAddress"/> and <see cref="UserAgent"/>, which are direct reads — because
    /// it derives from a User-Agent parse that is memoized in the host's <c>ICache</c>, which may be out of process.
    /// </remarks>
    ValueTask<string?> GetDeviceInfoAsync(CancellationToken cancellationToken = default);
}
