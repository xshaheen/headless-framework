// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Api.Identity.Authentication.ApiKey;

/// <summary>Handle the Api Key scheme authentication.</summary>
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
    // If no ApiKey is present on query string -> Return no result, let other handlers (if present) handle the request.
    // If the api_key is present but null or empty -> Return no result.
    // If the provided key does not exists -> Fail the authentication.
    // If the key is valid, create a new identity based on associated with key user
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // This line is important to correct working the multiple authentication schemes
        // when only cookies sent in request
        if (Context.User.Identity?.IsAuthenticated ?? false)
        {
            return AuthenticateResult.Success(new AuthenticationTicket(Context.User, "context.User"));
        }

        if (
            !Request.Headers.TryGetValue(Options.ApiKeyHeaderName, out var apiKeyValues)
            && !Request.Query.TryGetValue(Options.ApiKeyParamName, out apiKeyValues)
        )
        {
            return AuthenticateResult.NoResult();
        }

        var providedApiKey = apiKeyValues.FirstOrDefault();

        if (apiKeyValues.Count == 0 || string.IsNullOrWhiteSpace(providedApiKey))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKeyUser = await store.GetActiveApiKeyUserAsync(providedApiKey);

        if (apiKeyUser is null)
        {
            return AuthenticateResult.NoResult();
        }

        if (!await signInManager.CanSignInAsync(apiKeyUser))
        {
            return AuthenticateResult.Fail("Authentication failed.");
        }

        if (userManager.SupportsUserLockout && await userManager.IsLockedOutAsync(apiKeyUser))
        {
            return AuthenticateResult.Fail("Authentication failed.");
        }

        var claimsPrincipal = await signInManager.CreateUserPrincipalAsync(apiKeyUser);
        var ticket = new AuthenticationTicket(claimsPrincipal, Scheme.Name);

        return AuthenticateResult.Success(ticket);
    }
}
