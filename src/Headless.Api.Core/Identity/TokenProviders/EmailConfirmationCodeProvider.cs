// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Headless.Api.Identity.TokenProviders;

/// <summary>
/// Generates and validates 6-digit TOTP codes for email confirmation with configurable
/// timestep, variance, and hash algorithm. The modifier is bound to the user's email address.
/// </summary>
public sealed class EmailConfirmationCodeProvider<TUser>(
    TotpRfc6238Generator generator,
    IOptions<EmailConfirmationCodeProviderOptions> optionsAccessor
) : TotpTokenProvider<TUser>(generator, optionsAccessor.Value)
    where TUser : class
{
    private readonly EmailConfirmationCodeProviderOptions _emailOptions = optionsAccessor.Value;

    protected override async Task<string> GetUserModifierAsync(string purpose, UserManager<TUser> manager, TUser user)
    {
        Argument.IsNotNull(purpose);
        Argument.IsNotNull(manager);
        Argument.IsNotNull(user);

        var email =
            await manager.GetEmailAsync(user).ConfigureAwait(false)
            ?? throw new InvalidOperationException("The user does not have an email.");

        return $"{_emailOptions.Name}:{purpose}:{email}";
    }
}
