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
/// Scheme selection logic (evaluated on every request), by the handler each scheme was registered with rather than by
/// its name, so <c>AddApiKey("PartnerKey")</c> or a second JWT bearer scheme is selected too:
/// <list type="bullet">
///   <item><description>
///     A request carrying the API key header of an <c>AddApiKey</c> scheme, or its query-string parameter when that
///     scheme enables query keys → that API-key scheme.
///   </description></item>
///   <item><description>
///     <c>Authorization: Basic …</c> header present → the <c>AddBasicSchema</c> scheme.
///   </description></item>
///   <item><description>
///     Other <c>Authorization</c> header (e.g. <c>Bearer</c>) present → the scheme named <c>Bearer</c>, or else the
///     scheme registered with ASP.NET Core's <c>JwtBearerHandler</c>.
///   </description></item>
///   <item><description>
///     No auth credentials present → falls through to the registered default scheme.
///   </description></item>
/// </list>
/// <para>
/// When several schemes of the same kind match, the one under the canonical name (<c>ApiKey</c>, <c>Basic</c>,
/// <c>Bearer</c>) wins; with no canonical one among them the provider picks none and the configured default applies,
/// the way ASP.NET Core uses a lone registered scheme as the default but never guesses between several.
/// </para>
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

    // Rebuilt only when the registered schemes change: the base provider hands out the same array until a scheme is
    // added or removed, so the per-request cost is one reference comparison.
    private SchemeCatalog? _catalog;

    private async ValueTask<AuthenticationScheme?> _GetRequestSchemeAsync()
    {
        if (httpContextAccessor.HttpContext is null)
        {
            return null;
        }

        var request = httpContextAccessor.HttpContext.Request;
        var catalog = await _GetCatalogAsync().ConfigureAwait(false);

        var apiKeyMatches = catalog.ApiKey.Where(scheme => _CarriesApiKey(request, scheme)).ToList();

        if (apiKeyMatches.Count > 0)
        {
            return _Pick(apiKeyMatches, AuthenticationConstants.Schemas.ApiKey);
        }

        if (!request.Headers.TryGetValue(HttpHeaderNames.Authorization, out var value))
        {
            return null;
        }

        var isBasicAuth = value
            .ToString()
            .StartsWith(AuthenticationConstants.Schemas.Basic, StringComparison.OrdinalIgnoreCase);

        return isBasicAuth
            ? _Pick(catalog.Basic, AuthenticationConstants.Schemas.Basic)
            : _Pick(catalog.Bearer, AuthenticationConstants.Schemas.Bearer);
    }

    private bool _CarriesApiKey(HttpRequest request, AuthenticationScheme scheme)
    {
        var schemeOptions = apiKeySchemeOptions.Get(scheme.Name);

        return request.Headers.ContainsKey(schemeOptions.ApiKeyHeaderName)
            || (schemeOptions.AllowApiKeyInQueryString && request.Query.ContainsKey(schemeOptions.ApiKeyParamName));
    }

    /// <summary>
    /// Picks the scheme registered under <paramref name="canonicalName"/>, else the only candidate; several candidates
    /// without the canonical one are ambiguous, so none is picked and the configured default applies.
    /// </summary>
    private static AuthenticationScheme? _Pick(IReadOnlyList<AuthenticationScheme> candidates, string canonicalName)
    {
        foreach (var scheme in candidates)
        {
            if (string.Equals(scheme.Name, canonicalName, StringComparison.Ordinal))
            {
                return scheme;
            }
        }

        return candidates.Count == 1 ? candidates[0] : null;
    }

    private async ValueTask<SchemeCatalog> _GetCatalogAsync()
    {
        var schemes = await GetAllSchemesAsync().ConfigureAwait(false);
        var catalog = Volatile.Read(ref _catalog);

        if (catalog is not null && ReferenceEquals(catalog.Source, schemes))
        {
            return catalog;
        }

        catalog = SchemeCatalog.Create(schemes);
        Volatile.Write(ref _catalog, catalog);

        return catalog;
    }

    private sealed class SchemeCatalog
    {
        // The JWT bearer package is not a dependency of this one, so its handler is recognized by name. A handler
        // derived from it counts too.
        private const string _JwtBearerHandlerTypeName =
            "Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerHandler";

        public required IEnumerable<AuthenticationScheme> Source { get; init; }

        public required IReadOnlyList<AuthenticationScheme> ApiKey { get; init; }

        public required IReadOnlyList<AuthenticationScheme> Basic { get; init; }

        public required IReadOnlyList<AuthenticationScheme> Bearer { get; init; }

        public static SchemeCatalog Create(IEnumerable<AuthenticationScheme> schemes)
        {
            List<AuthenticationScheme> apiKey = [];
            List<AuthenticationScheme> basic = [];
            List<AuthenticationScheme> bearer = [];

            // A scheme under a canonical name keeps being selected whatever its handler, as it was before selection
            // looked at handlers.
            foreach (var scheme in schemes)
            {
                if (
                    _IsHandler(scheme.HandlerType, typeof(ApiKeyAuthenticationHandler<,>))
                    || _IsNamed(scheme, AuthenticationConstants.Schemas.ApiKey)
                )
                {
                    apiKey.Add(scheme);
                }
                else if (
                    _IsHandler(scheme.HandlerType, typeof(BasicAuthenticationHandler<,>))
                    || _IsNamed(scheme, AuthenticationConstants.Schemas.Basic)
                )
                {
                    basic.Add(scheme);
                }
                else if (
                    _IsJwtBearerHandler(scheme.HandlerType) || _IsNamed(scheme, AuthenticationConstants.Schemas.Bearer)
                )
                {
                    bearer.Add(scheme);
                }
            }

            return new SchemeCatalog
            {
                Source = schemes,
                ApiKey = apiKey,
                Basic = basic,
                Bearer = bearer,
            };
        }

        private static bool _IsNamed(AuthenticationScheme scheme, string name)
        {
            return string.Equals(scheme.Name, name, StringComparison.Ordinal);
        }

        private static bool _IsHandler(Type handlerType, Type openGenericHandler)
        {
            return handlerType.IsGenericType && handlerType.GetGenericTypeDefinition() == openGenericHandler;
        }

        private static bool _IsJwtBearerHandler(Type handlerType)
        {
            for (var type = handlerType; type is not null; type = type.BaseType)
            {
                if (string.Equals(type.FullName, _JwtBearerHandlerTypeName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
