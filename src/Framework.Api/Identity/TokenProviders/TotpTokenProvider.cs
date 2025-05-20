// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Framework.Api.Identity.TokenProviders;

public class TotpTokenProvider<TUser>(IOptions<TotpTokenProviderOptions> optionsAccessor) : IUserTwoFactorTokenProvider<TUser> where TUser : class
{
    private readonly TotpTokenProviderOptions _options = optionsAccessor.Value;

    public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<TUser> manager, TUser user) => Task.FromResult(false);

    public async Task<string> GenerateAsync(string purpose, UserManager<TUser> manager, TUser user)
    {
        Argument.IsNotNull(manager);

        var securityToken = await manager.CreateSecurityTokenAsync(user).ConfigureAwait(false);
        var modifier = await _GetUserModifierAsync(purpose, manager, user).ConfigureAwait(false);
        var code = TotpRfc6238Generator.GenerateCode(securityToken, modifier).ToString("D6", CultureInfo.InvariantCulture);

        return code;
    }

    public async Task<bool> ValidateAsync(string purpose, string token, UserManager<TUser> manager, TUser user)
    {
        Argument.IsNotNull(manager);

        if (!int.TryParse(token, CultureInfo.InvariantCulture, out var code))
        {
            return false;
        }

        var securityToken = await manager.CreateSecurityTokenAsync(user).ConfigureAwait(false);
        var modifier = await _GetUserModifierAsync(purpose, manager, user).ConfigureAwait(false);

        return TotpRfc6238Generator.ValidateCode(securityToken, code, modifier);
    }

    private async Task<string> _GetUserModifierAsync(string purpose, UserManager<TUser> manager, TUser user)
    {
        Argument.IsNotNull(purpose);
        Argument.IsNotNull(manager);
        Argument.IsNotNull(user);

        var userId = await manager.GetUserIdAsync(user).ConfigureAwait(false);

        return $"{_options.Name}:{purpose}:{userId}";
    }
}
