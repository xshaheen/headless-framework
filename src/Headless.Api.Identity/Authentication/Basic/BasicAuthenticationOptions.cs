// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Security;
using Microsoft.AspNetCore.Authentication;

namespace Headless.Api.Identity;

/// <summary>Options for the HTTP Basic authentication scheme.</summary>
[PublicAPI]
public sealed class BasicAuthenticationOptions
    : AuthenticationSchemeOptions,
        IHttpCredentialSchemeOptions,
        ICredentialCacheOptions
{
    /// <summary>Default scheme name used when none is specified during registration.</summary>
    public const string DefaultScheme = AuthenticationConstants.Schemas.Basic;

    /// <summary>
    /// How long a successful authentication is reused for the same user name and password without asking the Identity
    /// store again. <see langword="null"/> (the default) or <see cref="TimeSpan.Zero"/> turns the cache off, so every
    /// request runs the full check. At most one hour.
    /// </summary>
    /// <remarks>
    /// <para>
    /// While an entry lives, a request with the same user name and password skips the user lookup, the password hash
    /// check (PBKDF2, tens of milliseconds of CPU per request), the sign-in and lockout checks, and the claims and role
    /// queries, and gets its own copy of the principal the first request built. It also skips the reset of the user's
    /// failed-attempt count that a successful password check makes, so failed attempts from elsewhere add up toward
    /// lockout until an entry expires and the full check runs again. The entry expires a fixed time after the request
    /// that created it; hits do not extend it.
    /// </para>
    /// <para>
    /// <strong>Trade-off:</strong> the old password keeps authenticating until its entry expires after it changes, and
    /// so does a user who has since been locked out or disabled, or whose roles or claims changed. Pick a lifetime the
    /// application can accept for that delay.
    /// </para>
    /// <para>
    /// Only successful authentications are cached: a rejected user name and password always runs the full check, so
    /// Identity still counts it toward lockout. The cache is in-process and keyed by an HMAC of the scheme and the
    /// credentials under a per-process random key, so it never holds the password and does not survive a restart.
    /// </para>
    /// </remarks>
    public TimeSpan? CredentialCacheDuration { get; set; }

    /// <summary>
    /// Maximum number of cached authentications this scheme holds while <see cref="CredentialCacheDuration"/> is on.
    /// Default <c>1000</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A success that arrives while the cache is full is not cached; the cache then evicts expired entries, then the
    /// least recently used ones, in the background to make room.
    /// </para>
    /// </remarks>
    public int CredentialCacheMaxEntries { get; set; } = 1000;

    /// <inheritdoc/>
    /// <remarks>The <c>Authorization</c> header under the <c>Basic</c> scheme (RFC 7617).</remarks>
    public IReadOnlyList<HttpCredentialDescription> Credentials { get; } =
    [
        HttpCredentialDescription.Authorization(
            "Basic",
            "The user name and password, joined by a colon and Base64-encoded, in the Authorization header."
        ),
    ];
}

internal sealed class BasicAuthenticationOptionsValidator : AbstractValidator<BasicAuthenticationOptions>
{
    public BasicAuthenticationOptionsValidator()
    {
        Include(new CredentialCacheOptionsValidator());
    }
}
