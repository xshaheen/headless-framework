// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api.Identity.Authentication.ApiKey;
using Headless.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Headless.Api.Identity.Schemes;

/// <summary>
/// An <see cref="AuthenticationSchemeProvider"/> that automatically selects the correct scheme
/// based on the incoming request, so plain <c>[Authorize]</c> works without needing
/// <c>[Authorize(AuthenticationSchemes = "Bearer")]</c> on every controller.
/// </summary>
/// <remarks>
/// Scheme selection logic (evaluated on every request):
/// <list type="bullet">
///   <item><description>
///     API key header / query-string parameter present → <c>ApiKey</c> scheme.
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
/// </remarks>
public sealed class DynamicAuthenticationSchemeProvider(
    IHttpContextAccessor httpContextAccessor,
    IOptions<AuthenticationOptions> options,
    IOptions<ApiKeyAuthenticationSchemeOptions> apiKeySchemeOptions
) : AuthenticationSchemeProvider(options)
{
    private readonly ApiKeyAuthenticationSchemeOptions _apiKeyAuthenticationOptions = apiKeySchemeOptions.Value;

    public override async Task<AuthenticationScheme?> GetDefaultAuthenticateSchemeAsync()
    {
        return await _GetRequestSchemeAsync() ?? await base.GetDefaultAuthenticateSchemeAsync();
    }

    public override async Task<AuthenticationScheme?> GetDefaultChallengeSchemeAsync()
    {
        return await _GetRequestSchemeAsync() ?? await base.GetDefaultChallengeSchemeAsync();
    }

    public override async Task<AuthenticationScheme?> GetDefaultForbidSchemeAsync()
    {
        return await _GetRequestSchemeAsync() ?? await base.GetDefaultForbidSchemeAsync();
    }

    public override async Task<AuthenticationScheme?> GetDefaultSignInSchemeAsync()
    {
        return await _GetRequestSchemeAsync() ?? await base.GetDefaultSignInSchemeAsync();
    }

    public override async Task<AuthenticationScheme?> GetDefaultSignOutSchemeAsync()
    {
        return await _GetRequestSchemeAsync() ?? await base.GetDefaultSignOutSchemeAsync();
    }

    private async ValueTask<AuthenticationScheme?> _GetRequestSchemeAsync()
    {
        if (httpContextAccessor.HttpContext is null)
        {
            return null;
        }

        var request = httpContextAccessor.HttpContext.Request;

        var isApiKey =
            request.Headers.ContainsKey(_apiKeyAuthenticationOptions.ApiKeyHeaderName)
            || request.Query.ContainsKey(_apiKeyAuthenticationOptions.ApiKeyParamName);

        if (isApiKey)
        {
            return await GetSchemeAsync(AuthenticationConstants.Schemas.ApiKey);
        }

        var hasAuthorizationHeader = request.Headers.TryGetValue(HttpHeaderNames.Authorization, out var value);

        if (hasAuthorizationHeader)
        {
            var isBasicAuth = value
                .ToString()
                .StartsWith(AuthenticationConstants.Schemas.Basic, StringComparison.OrdinalIgnoreCase);

            return isBasicAuth
                ? await GetSchemeAsync(AuthenticationConstants.Schemas.Basic)
                : await GetSchemeAsync(AuthenticationConstants.Schemas.Bearer);
        }

        return null;
    }
}
