// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard.Authentication;

/// <summary>
/// The authentication scheme a dashboard registers under its authentication name. It checks the dashboard's own
/// credential through the <see cref="IAuthService"/> keyed by that name, and the dashboard's authorization policy of
/// the same name names this scheme, so the host's <c>UseAuthorization</c> evaluates the credential before the endpoint
/// or hub runs.
/// </summary>
/// <remarks>
/// It authenticates only requests routed to an endpoint whose <see cref="DashboardEndpointMetadata"/> names this
/// scheme. A host with no scheme of its own makes this one its default, and the host's <c>UseAuthentication</c> must
/// then neither check nor log a dashboard credential on its own requests.
/// </remarks>
internal sealed class DashboardAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder
) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    private readonly ILogger _failureLogger = loggerFactory.CreateLogger<DashboardAuthenticationHandler>();

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var metadata = Context.GetEndpoint()?.Metadata.GetMetadata<DashboardEndpointMetadata>();
        if (!string.Equals(metadata?.AuthenticationName, Scheme.Name, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        var authService = Context.RequestServices.GetRequiredKeyedService<IAuthService>(Scheme.Name);
        var result = await authService.AuthenticateAsync(Context, Context.RequestAborted).ConfigureAwait(false);

        if (!result.IsAuthenticated)
        {
            // Path only, never the query string: a hub client sends its credential as access_token.
            _failureLogger.LogAuthenticationFailed(_SanitizeForLog(Context.Request.Path.Value), result.ErrorMessage);
            return AuthenticateResult.Fail(result.ErrorMessage ?? "Authentication failed");
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, result.Username!)], Scheme.Name);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    // CR/LF stripped so a user-controlled path cannot forge log entries.
    private static string _SanitizeForLog(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
    }
}

internal static partial class DashboardAuthenticationHandlerLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "AuthenticationFailed",
        Level = LogLevel.Warning,
        Message = "Authentication failed for {Path}: {Error}"
    )]
    internal static partial void LogAuthenticationFailed(this ILogger logger, string path, string? error);
}
