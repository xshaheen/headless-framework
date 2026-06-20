// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Api.Identity.Authentication.ApiKey;

/// <summary>
/// ASP.NET Core authentication handler for the API key scheme.
/// Reads the key from the <c>X-API-Key</c> header (or an optional query-string parameter),
/// resolves the associated user via <see cref="IApiKeyStore{TUser,TUserId}"/>, and
/// issues an <see cref="Microsoft.AspNetCore.Authentication.AuthenticationTicket"/> on success.
/// </summary>
/// <remarks>
/// Authentication flow:
/// <list type="number">
///   <item><description>If the request already carries an authenticated identity, the existing ticket is reused.</description></item>
///   <item><description>If no API key is present in the header (or query string when <see cref="ApiKeyAuthenticationSchemeOptions.AllowApiKeyInQueryString"/> is <see langword="true"/>), <c>NoResult</c> is returned so other handlers can run.</description></item>
///   <item><description>If the key is blank or not found in the store, <c>NoResult</c> is returned.</description></item>
///   <item><description>If the user cannot sign in (email not confirmed, locked out, etc.) <c>Fail</c> is returned.</description></item>
/// </list>
/// </remarks>
/// <typeparam name="TUser">The user type, derived from <see cref="IdentityUser{TKey}"/>.</typeparam>
/// <typeparam name="TUserId">The type of the user's primary key.</typeparam>
public sealed class ApiKeyAuthenticationHandler<TUser, TUserId>(
    IOptionsMonitor<ApiKeyAuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    UserManager<TUser> userManager,
    SignInManager<TUser> signInManager,
    IApiKeyStore<TUser, TUserId> store
) : AuthenticationHandler<ApiKeyAuthenticationSchemeOptions>(options, logger, encoder)
    where TUser : IdentityUser<TUserId>
    where TUserId : IEquatable<TUserId>
{
    // This method gets called for every request that requires authentication.
    // The logic goes something like this:
    // If no ApiKey is present on the header (or query string when AllowApiKeyInQueryString is true) -> Return no result,
    //   let other handlers (if present) handle the request.
    // If the api_key is present but null or empty -> Return no result.
    // If the provided key does not exist -> Return no result (key not found).
    // If the key is valid, create a new identity based on the user associated with the key.
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // This line is important to correct working the multiple authentication schemes
        // when only cookies sent in request
        if (Context.User.Identity?.IsAuthenticated ?? false)
        {
            return AuthenticateResult.Success(new AuthenticationTicket(Context.User, "context.User"));
        }

        var foundInHeader = Request.Headers.TryGetValue(Options.ApiKeyHeaderName, out var apiKeyValues);

        if (!foundInHeader && Options.AllowApiKeyInQueryString)
        {
            // Query-string fallback is opt-in: keys passed via the URL are visible in server access logs,
            // CDN/proxy logs, and Referer headers, which risks unintentional exposure.
            Request.Query.TryGetValue(Options.ApiKeyParamName, out apiKeyValues);
        }

        var providedApiKey = apiKeyValues.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(providedApiKey))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKeyUser = await store
            .GetActiveApiKeyUserAsync(providedApiKey, Context.RequestAborted)
            .ConfigureAwait(false);

        if (apiKeyUser is null)
        {
            return AuthenticateResult.NoResult();
        }

        if (!await signInManager.CanSignInAsync(apiKeyUser).ConfigureAwait(false))
        {
            return AuthenticateResult.Fail("Authentication failed.");
        }

        if (userManager.SupportsUserLockout && await userManager.IsLockedOutAsync(apiKeyUser).ConfigureAwait(false))
        {
            return AuthenticateResult.Fail("Authentication failed.");
        }

        var claimsPrincipal = await signInManager.CreateUserPrincipalAsync(apiKeyUser).ConfigureAwait(false);
        var ticket = new AuthenticationTicket(claimsPrincipal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
