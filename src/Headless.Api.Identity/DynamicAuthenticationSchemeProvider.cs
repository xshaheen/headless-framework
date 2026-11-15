// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Http;
using Headless.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Headless.Api.Identity;

/// <summary>
/// An <see cref="AuthenticationSchemeProvider"/> that automatically selects the correct scheme
/// based on the incoming request, so plain <c>[Authorize]</c> works without needing
/// <c>[Authorize(AuthenticationSchemes = "Bearer")]</c> on every controller.
/// </summary>
/// <remarks>
/// Scheme selection logic (evaluated on every request):
/// <list type="bullet">
///   <item><description>
///     API key header present, or query-string parameter present when query keys are enabled → <c>ApiKey</c> scheme.
///   </description></item>
///   <item><description>
///     <c>Authorization: Basic …</c> header present → <c>Basic</c> scheme.
///   </description></item>
///   <item><description>
///     Other <c>Authorization</c> header (e.g. <c>Bearer</c>) present → <c>Bearer</c> scheme.
///   </description></item>
///   <item><description>
///     No auth credentials present → falls through to the registered default scheme.
///   </description></item>
/// </list>
/// <para>
/// Only the authenticate, challenge, and forbid defaults follow the request. The sign-in and sign-out defaults stay the
/// configured ones: none of the header schemes can sign in or out, so a <c>SignOutAsync()</c> on a request that
/// happens to carry an API key or bearer token would otherwise fail.
/// </para>
/// <para>
/// <c>AddBasicSchema</c> and <c>AddApiKey</c> register this provider only in place of the stock
/// <see cref="AuthenticationSchemeProvider"/>; a scheme provider the application registered itself is kept.
/// </para>
/// </remarks>
public sealed class DynamicAuthenticationSchemeProvider(
    IHttpContextAccessor httpContextAccessor,
    IOptions<AuthenticationOptions> options,
    IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> apiKeySchemeOptions
) : AuthenticationSchemeProvider(options)
{
    /// <inheritdoc/>
    /// <remarks>Selects the scheme based on request credentials before falling back to the configured default.</remarks>
    public override async Task<AuthenticationScheme?> GetDefaultAuthenticateSchemeAsync()
    {
        return await _GetRequestSchemeAsync().ConfigureAwait(false)
            ?? await base.GetDefaultAuthenticateSchemeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>Selects the scheme based on request credentials before falling back to the configured default.</remarks>
    public override async Task<AuthenticationScheme?> GetDefaultChallengeSchemeAsync()
    {
        return await _GetRequestSchemeAsync().ConfigureAwait(false)
            ?? await base.GetDefaultChallengeSchemeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>Selects the scheme based on request credentials before falling back to the configured default.</remarks>
    public override async Task<AuthenticationScheme?> GetDefaultForbidSchemeAsync()
    {
        return await _GetRequestSchemeAsync().ConfigureAwait(false)
            ?? await base.GetDefaultForbidSchemeAsync().ConfigureAwait(false);
    }

    private async ValueTask<AuthenticationScheme?> _GetRequestSchemeAsync()
    {
        if (httpContextAccessor.HttpContext is null)
        {
            return null;
        }

        var request = httpContextAccessor.HttpContext.Request;

        var apiKeyOptions = apiKeySchemeOptions.Get(AuthenticationConstants.Schemas.ApiKey);
        var isApiKey =
            request.Headers.ContainsKey(apiKeyOptions.ApiKeyHeaderName)
            || (apiKeyOptions.AllowApiKeyInQueryString && request.Query.ContainsKey(apiKeyOptions.ApiKeyParamName));

        if (isApiKey)
        {
            return await GetSchemeAsync(AuthenticationConstants.Schemas.ApiKey).ConfigureAwait(false);
        }

        var hasAuthorizationHeader = request.Headers.TryGetValue(HttpHeaderNames.Authorization, out var value);

        if (hasAuthorizationHeader)
        {
            var isBasicAuth = value
                .ToString()
                .StartsWith(AuthenticationConstants.Schemas.Basic, StringComparison.OrdinalIgnoreCase);

            return isBasicAuth
                ? await GetSchemeAsync(AuthenticationConstants.Schemas.Basic).ConfigureAwait(false)
                : await GetSchemeAsync(AuthenticationConstants.Schemas.Bearer).ConfigureAwait(false);
        }

        return null;
    }
}
