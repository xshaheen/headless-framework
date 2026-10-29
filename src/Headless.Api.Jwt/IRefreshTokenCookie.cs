// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Security;

/// <summary>
/// Writes, reads, and deletes the refresh-token cookie of the current HTTP request, with the attributes
/// <see cref="RefreshTokenCookieOptions"/> configures plus <c>Secure</c> and <c>HttpOnly</c>, which are always set.
/// </summary>
/// <remarks>
/// The cookie only carries the token. Issuing, storing, rotating, and revoking refresh tokens stay with the
/// application. A browser sends the cookie on cross-site requests too, so guard every endpoint that reads it with
/// <c>RequireSameOrigin()</c> or <c>[RequireSameOrigin]</c>. Each member needs an active request and throws
/// <see cref="InvalidOperationException"/> outside one.
/// </remarks>
[PublicAPI]
public interface IRefreshTokenCookie
{
    /// <summary>Sets the cookie to <paramref name="token"/>, expiring at <paramref name="expiresAt"/>.</summary>
    /// <param name="token">The refresh token. Must not be empty.</param>
    /// <param name="expiresAt">
    /// When the browser drops the cookie, normally the refresh token's own expiry. Must be later than the registered
    /// <see cref="TimeProvider"/>'s current time.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="token"/> is <see langword="null"/> or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="expiresAt"/> is not in the future.</exception>
    /// <exception cref="InvalidOperationException">No HTTP request is active.</exception>
    void Write(string token, DateTimeOffset expiresAt);

    /// <summary>Returns the cookie's value on the current request, or <see langword="null"/> when it is absent or empty.</summary>
    /// <exception cref="InvalidOperationException">No HTTP request is active.</exception>
    string? Read();

    /// <summary>
    /// Expires the cookie in the browser, using the same <c>Path</c> and <c>Domain</c> it was written with so the
    /// browser matches and removes it.
    /// </summary>
    /// <exception cref="InvalidOperationException">No HTTP request is active.</exception>
    void Delete();
}
