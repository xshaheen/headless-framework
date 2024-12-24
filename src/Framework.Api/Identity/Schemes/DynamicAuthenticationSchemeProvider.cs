// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Api.Identity.Authentication.ApiKey;
using Framework.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Framework.Api.Identity.Schemes;

/// <summary>
/// This custom provider allows able to use just [Authorize] instead of having to define [Authorize(AuthenticationSchemes = "Bearer")]
/// above every API controller without this Bearer authorization will not work
/// </summary>
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
