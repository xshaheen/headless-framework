// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Text.Encodings.Web;
using Framework.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Api.Identity.Authentication.Basic;

public sealed class BasicAuthenticationHandler<TUser, TUserId>(
    IOptionsMonitor<BasicAuthenticationOptions> options,
    ILoggerFactory loggerFactory,
    ILogger<BasicAuthenticationHandler<TUser, TUserId>> logger,
    UrlEncoder encoder,
    UserManager<TUser> userManager,
    SignInManager<TUser> signInManager
) : AuthenticationHandler<BasicAuthenticationOptions>(options, loggerFactory, encoder)
    where TUser : IdentityUser<TUserId>
    where TUserId : IEquatable<TUserId>
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // This block is important when working with multiple authentication schemes
        // when only cookies are being sent in request.
        if (Context.User.Identity?.IsAuthenticated ?? false)
        {
            return AuthenticateResult.Success(new AuthenticationTicket(Context.User, "context.User"));
        }

        if (!_TryGetEncodedCredentials(out var encodedCredentials))
        {
            return AuthenticateResult.NoResult();
        }

        if (!_TryDecodeCredentials(encodedCredentials, out var userName, out var password))
        {
            return AuthenticateResult.Fail("Invalid Authorization header value.");
        }

        var user = await userManager.FindByNameAsync(userName);

        if (
            user is null
            || !await signInManager.CanSignInAsync(user)
            || (userManager.SupportsUserLockout && await userManager.IsLockedOutAsync(user))
            || !await userManager.CheckPasswordAsync(user, password)
        )
        {
            return AuthenticateResult.Fail("Invalid user name or password.");
        }

        var claimsPrincipal = await signInManager.CreateUserPrincipalAsync(user);
        var ticket = new AuthenticationTicket(claimsPrincipal, Options.Scheme);

        return AuthenticateResult.Success(ticket);
    }

    #region Helpers

    private bool _TryGetEncodedCredentials([NotNullWhen(true)] out string? encodedCredentials)
    {
        encodedCredentials = null;

        if (Request.Headers.TryGetValue(HttpHeaderNames.Authorization, out var values))
        {
            var headerValue = values.ToString();
            const string scheme = "Basic ";

            if (headerValue.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            {
                encodedCredentials = headerValue[scheme.Length..].Trim();
            }
        }

        return !string.IsNullOrEmpty(encodedCredentials);
    }

    private bool _TryDecodeCredentials(
        string encodedCredentials,
        [NotNullWhen(true)] out string? userName,
        [NotNullWhen(true)] out string? password
    )
    {
        userName = null;
        password = null;

        try
        {
            var decodedCredentials = encodedCredentials.DecodeBase64();
            var separatorPosition = decodedCredentials.IndexOf(':', StringComparison.Ordinal);

            if (separatorPosition >= 0)
            {
                userName = decodedCredentials[..separatorPosition];
                password = decodedCredentials[(separatorPosition + 1)..];
            }
        }
        catch (Exception e)
        {
            if (logger.IsEnabled(LogLevel.Trace))
            {
                logger.LogError(e, "Failed to decode credentials.");
            }
        }

        return userName is not null && password is not null;
    }

    #endregion
}
