// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using System.Text.RegularExpressions;
using FluentValidation;
using Microsoft.AspNetCore.Http;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// Configures a dashboard under <see cref="AuthMode.Host"/>: which host policies let a user view and operate it, how
/// a host user who is not signed in is sent to sign in, and how long the dashboard's own session lasts.
/// </summary>
/// <remarks>
/// <para>
/// The host signs the operator in once. The dashboard then copies that user's identities and claims into its own
/// session cookie, and every dashboard request authenticates with that cookie alone. The view and operate policies
/// are evaluated against the copied claims, so they must be satisfiable from claims: a requirement that reads the
/// host's bearer token or a request header fails under the session. A policy that lists authentication schemes
/// passes only for a session issued from one of those schemes.
/// </para>
/// <para>
/// The session is a cookie protected by the host's Data Protection key ring. Every instance serving the dashboard
/// must share that key ring, or a session issued by one instance is refused by the next.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class DashboardHostAuthenticationOptions
{
    /// <summary>The default <see cref="SessionIdleTimeout"/>: 30 minutes.</summary>
    public static readonly TimeSpan DefaultSessionIdleTimeout = TimeSpan.FromMinutes(30);

    /// <summary>The default <see cref="SessionLifetime"/>: 8 hours.</summary>
    public static readonly TimeSpan DefaultSessionLifetime = TimeSpan.FromHours(8);

    /// <summary>
    /// Gets or sets the host authorization policy a user must pass to open the dashboard and read from it. When
    /// <see langword="null"/> or empty, the host's default policy applies.
    /// </summary>
    public string? ViewPolicy { get; set; }

    /// <summary>
    /// Gets or sets the host authorization policy a user must also pass to run operator actions, the dashboard's
    /// state-changing requests. When <see langword="null"/> or empty, the view policy applies, so viewing is enough.
    /// </summary>
    public string? OperatePolicy { get; set; }

    /// <summary>
    /// Gets or sets the host authentication scheme challenged when a user who is not signed in to the host opens the
    /// dashboard's sign-in page, for example an OpenID Connect or cookie scheme. When <see langword="null"/> or
    /// empty, the dashboard offers no sign-in redirect, and a host user signs in with a bearer token through the
    /// session exchange instead.
    /// </summary>
    public string? ChallengeScheme { get; set; }

    /// <summary>
    /// Gets or sets the name of the dashboard's session cookie. When <see langword="null"/> or empty, the name is
    /// <c>__Host-</c> followed by the dashboard's authentication name in lower case. Set it when two applications on
    /// one host name mount the same dashboard, so their sessions do not overwrite each other.
    /// </summary>
    /// <remarks>
    /// The cookie is always host-only and written with <c>Path=/</c>, <c>Secure</c>, <c>HttpOnly</c>, and
    /// <c>SameSite=Strict</c>, so a <c>__Host-</c> name satisfies the browser's prefix rules.
    /// </remarks>
    public string? SessionCookieName { get; set; }

    /// <summary>
    /// Gets or sets how long a session survives without an authenticated dashboard request. A request made after half
    /// of this window has passed renews it. Defaults to <see cref="DefaultSessionIdleTimeout"/>.
    /// </summary>
    public TimeSpan SessionIdleTimeout { get; set; } = DefaultSessionIdleTimeout;

    /// <summary>
    /// Gets or sets the absolute lifetime of a session, counted from sign-in whatever its activity. Defaults to
    /// <see cref="DefaultSessionLifetime"/>.
    /// </summary>
    public TimeSpan SessionLifetime { get; set; } = DefaultSessionLifetime;

    /// <summary>
    /// Gets or sets how often <see cref="Revalidate"/> runs for one session. Required when
    /// <see cref="Revalidate"/> is set.
    /// </summary>
    public TimeSpan? RevalidationInterval { get; set; }

    /// <summary>
    /// Gets or sets a callback that confirms a session's user is still allowed in, for example that the account is
    /// not disabled. It receives the request and the session's principal, runs at most once per
    /// <see cref="RevalidationInterval"/> for a session, and ends the session when it returns <see langword="false"/>.
    /// </summary>
    public Func<HttpContext, ClaimsPrincipal, ValueTask<bool>>? Revalidate { get; set; }
}

/// <summary>Validates <see cref="DashboardHostAuthenticationOptions"/> when the dashboard runs in host mode.</summary>
internal sealed partial class DashboardHostAuthenticationOptionsValidator
    : AbstractValidator<DashboardHostAuthenticationOptions>
{
    public DashboardHostAuthenticationOptionsValidator()
    {
        RuleFor(x => x.SessionIdleTimeout).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.SessionLifetime).GreaterThan(TimeSpan.Zero);
        RuleFor(x => x.SessionIdleTimeout)
            .LessThanOrEqualTo(x => x.SessionLifetime)
            .WithMessage("SessionIdleTimeout must not exceed SessionLifetime.");

        RuleFor(x => x.RevalidationInterval)
            .NotNull()
            .When(x => x.Revalidate is not null)
            .WithMessage("RevalidationInterval is required when Revalidate is set.");
        RuleFor(x => x.RevalidationInterval)
            .Must(interval => interval > TimeSpan.Zero)
            .When(x => x.RevalidationInterval.HasValue)
            .WithMessage("RevalidationInterval must be positive.");

        RuleFor(x => x.SessionCookieName)
            .Must(name => _CookieNameRegex.IsMatch(name!))
            .When(x => !string.IsNullOrEmpty(x.SessionCookieName))
            .WithMessage("SessionCookieName must be a cookie name token: letters, digits, and !#$%&'*+-.^_`|~.");
    }

    // RFC 6265 cookie names are RFC 7230 tokens; anything else is dropped or mangled by browsers.
    [GeneratedRegex(@"^[A-Za-z0-9!#$%&'*+\-.^_`|~]+$", RegexOptions.None, matchTimeoutMilliseconds: 100)]
    private static partial Regex _CookieNameRegex { get; }
}
