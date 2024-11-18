// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Framework.Api.Identity.TokenProviders;

/// <summary>
/// Generate 6-digit code for email confirmation purpose.
/// NOTE: This use <see cref="TotpSecurityStampBasedTokenProvider{TUser}"/> for generate the token will have
/// a fixed 9-minute lifetime and not configurable. See: <a href="https://github.com/dotnet/aspnetcore/issues/27088"/> and
/// <a href="https://github.com/dotnet/aspnetcore/issues/13347" />
/// </summary>
/// <typeparam name="TUser">The type of the user.</typeparam>
public sealed class EmailConfirmationCodeProvider<TUser>(IOptions<EmailConfirmationCodeProviderOptions> optionsAccessor)
    : TotpSecurityStampBasedTokenProvider<TUser>
    where TUser : class
{
    private readonly EmailConfirmationCodeProviderOptions _options = optionsAccessor.Value;

    public override async Task<string> GetUserModifierAsync(string purpose, UserManager<TUser> manager, TUser user)
    {
        Argument.IsNotNull(manager);
        Argument.IsNotNull(user);
        Argument.IsNotNull(purpose);

        var email =
            await manager.GetEmailAsync(user)
            ?? throw new InvalidOperationException("The user does not have an email.");

        return $"{_options.Name}:{purpose}:{email}";
    }

    public override Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<TUser> manager, TUser user)
    {
        return Task.FromResult(false);
    }
}
