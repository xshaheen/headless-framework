// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Headless.Api.Identity.TokenProviders;

/// <summary>
/// Generates and validates 6-digit TOTP codes for email confirmation with configurable
/// timestep, variance, and hash algorithm. The modifier is bound to the user's email address
/// rather than the user ID, so the code is invalidated if the email changes.
/// </summary>
/// <typeparam name="TUser">The user type managed by ASP.NET Core Identity.</typeparam>
public sealed class EmailConfirmationCodeProvider<TUser>(
    TotpRfc6238Generator generator,
    IOptions<EmailConfirmationCodeProviderOptions> optionsAccessor
) : TotpTokenProvider<TUser>(generator, optionsAccessor.Value)
    where TUser : class
{
    private readonly EmailConfirmationCodeProviderOptions _emailOptions = optionsAccessor.Value;

    /// <summary>
    /// Builds the TOTP modifier using the user's email address instead of the user ID.
    /// Format: <c>{Name}:{purpose}:{email}</c>.
    /// </summary>
    /// <param name="purpose">The intended purpose (e.g. <c>"EmailConfirmation"</c>).</param>
    /// <param name="manager">The <see cref="UserManager{TUser}"/> used to retrieve the user's email.</param>
    /// <param name="user">The user for whom the modifier is built.</param>
    /// <returns>A string that uniquely identifies the user-purpose combination via the user's email.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="purpose"/>, <paramref name="manager"/>, or <paramref name="user"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="InvalidOperationException">The user does not have an email address set.</exception>
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
