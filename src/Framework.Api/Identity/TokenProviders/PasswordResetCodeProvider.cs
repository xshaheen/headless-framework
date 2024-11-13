// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Kernel.Checks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Framework.Api.Identity.TokenProviders;

/// <summary>
/// Generate 6-digit code for password reset purpose.
/// NOTE: This use <see cref="TotpSecurityStampBasedTokenProvider{TUser}"/> for generate the token will have
/// a fixed 9-minute lifetime and not configurable. See: <a href="https://github.com/dotnet/aspnetcore/issues/27088"/> and
/// <a href="https://github.com/dotnet/aspnetcore/issues/13347" />
/// </summary>
/// <typeparam name="TUser">The type of the user.</typeparam>
public sealed class PasswordResetCodeProvider<TUser>(IOptions<PasswordResetCodeProviderOptions> optionsAccessor)
    : TotpSecurityStampBasedTokenProvider<TUser>
    where TUser : class
{
    private readonly PasswordResetCodeProviderOptions _options = optionsAccessor.Value;

    public override async Task<string> GetUserModifierAsync(string purpose, UserManager<TUser> manager, TUser user)
    {
        Argument.IsNotNull(manager);
        Argument.IsNotNull(user);
        Argument.IsNotNull(purpose);

        var userId = await manager.GetUserIdAsync(user);

        return $"{_options.Name}:{purpose}:{userId}";
    }

    public override Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<TUser> manager, TUser user)
    {
        return Task.FromResult(false);
    }
}
