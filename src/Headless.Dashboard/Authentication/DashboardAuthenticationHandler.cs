// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Headless.Dashboard;

/// <summary>
/// The authentication scheme a dashboard registers under its authentication name. It checks the dashboard's own
/// credential through the <see cref="IAuthService"/> keyed by that name, and the dashboard's authorization policy of
/// the same name names this scheme, so the host's <c>UseAuthorization</c> evaluates the credential before the endpoint
/// or hub runs.
/// </summary>
/// <remarks>
/// <para>
/// It authenticates only requests routed to an endpoint whose <see cref="DashboardEndpointMetadata"/> names this
/// scheme. A host with no scheme of its own makes this one its default, and the host's <c>UseAuthentication</c> must
/// then neither check nor log a dashboard credential on its own requests.
/// </para>
/// <para>
/// It implements <see cref="IAuthenticationHandler"/> directly rather than deriving from
/// <c>AuthenticationHandler&lt;TOptions&gt;</c>, which keeps its first result for the whole request. When that default
/// scheme runs before routing, as a <c>WebApplication</c> places its implicit <c>UseAuthentication</c> ahead of a
/// host's own <c>UseRouting</c>, the first call sees no endpoint; caching that answer would refuse the right
/// credential when the dashboard policy asks again. Only a result computed for a routed request is kept, so a
/// credential is still checked, and a failure logged, once per request.
/// </para>
/// </remarks>
internal sealed class DashboardAuthenticationHandler(ILogger<DashboardAuthenticationHandler> logger)
    : IAuthenticationHandler
{
    private AuthenticationScheme _scheme = null!;
    private HttpContext _context = null!;
    private Task<AuthenticateResult>? _routedResult;

    public Task InitializeAsync(AuthenticationScheme scheme, HttpContext context)
    {
        _scheme = scheme;
        _context = context;
        return Task.CompletedTask;
    }

    public Task<AuthenticateResult> AuthenticateAsync()
    {
        var endpoint = _context.GetEndpoint();
        if (endpoint is null)
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        return _routedResult ??= _AuthenticateRoutedAsync(endpoint);
    }

    public Task ChallengeAsync(AuthenticationProperties? properties)
    {
        _context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public Task ForbidAsync(AuthenticationProperties? properties)
    {
        _context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    private async Task<AuthenticateResult> _AuthenticateRoutedAsync(Endpoint endpoint)
    {
        var metadata = endpoint.Metadata.GetMetadata<DashboardEndpointMetadata>();
        if (!string.Equals(metadata?.AuthenticationName, _scheme.Name, StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        if (metadata is { AcceptsSession: true } && StringValues.IsNullOrEmpty(_context.Request.Headers.Authorization))
        {
            return AuthenticateResult.NoResult();
        }

        var authService = _context.RequestServices.GetRequiredKeyedService<IAuthService>(_scheme.Name);
        var result = await authService.AuthenticateAsync(_context, _context.RequestAborted).ConfigureAwait(false);

        if (!result.IsAuthenticated)
        {
            // Path only, never the query string: a hub client sends its credential as access_token.
            logger.LogAuthenticationFailed(_SanitizeForLog(_context.Request.Path.Value), result.ErrorMessage);
            return AuthenticateResult.Fail(result.ErrorMessage ?? "Authentication failed");
        }

        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, result.Username!)], _scheme.Name);

        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), _scheme.Name));
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
        EventId = 2231,
        EventName = "AuthenticationFailed",
        Level = LogLevel.Warning,
        Message = "Authentication failed for {Path}: {Error}"
    )]
    internal static partial void LogAuthenticationFailed(this ILogger logger, string path, string? error);
}
