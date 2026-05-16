// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Headless.Api.Identity.TokenProviders;

/// <summary>
/// Base TOTP token provider with configurable timestep, variance, and hash algorithm.
/// Derive from this class and override <see cref="GetUserModifierAsync"/> to customize
/// the modifier used for code generation and validation.
/// </summary>
public class TotpTokenProvider<TUser> : IUserTwoFactorTokenProvider<TUser>
    where TUser : class
{
    private readonly TotpRfc6238Generator _generator;
    private readonly TotpTokenProviderOptions _options;

    public TotpTokenProvider(TotpRfc6238Generator generator, IOptions<TotpTokenProviderOptions> optionsAccessor)
        : this(generator, optionsAccessor.Value) { }

    protected TotpTokenProvider(TotpRfc6238Generator generator, TotpTokenProviderOptions options)
    {
        _generator = generator;
        _options = options;
    }

    public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<TUser> manager, TUser user) => Task.FromResult(false);

    public async Task<string> GenerateAsync(string purpose, UserManager<TUser> manager, TUser user)
    {
        Argument.IsNotNull(manager);

        var securityToken = await manager.CreateSecurityTokenAsync(user).ConfigureAwait(false);
        var modifier = await GetUserModifierAsync(purpose, manager, user).ConfigureAwait(false);
        var code = _generator
            .GenerateCode(securityToken, _options.Timestep, modifier, _options.HashMode)
            .ToString("D6", CultureInfo.InvariantCulture);

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
        var modifier = await GetUserModifierAsync(purpose, manager, user).ConfigureAwait(false);

        return _generator.ValidateCode(
            securityToken,
            code,
            _options.Timestep,
            _options.Variance,
            modifier,
            _options.HashMode
        );
    }

    /// <summary>
    /// Builds the modifier string that binds the TOTP code to a specific user and purpose.
    /// Default: <c>{Name}:{purpose}:{userId}</c>.
    /// </summary>
    protected virtual async Task<string> GetUserModifierAsync(string purpose, UserManager<TUser> manager, TUser user)
    {
        Argument.IsNotNull(purpose);
        Argument.IsNotNull(manager);
        Argument.IsNotNull(user);

        var userId = await manager.GetUserIdAsync(user).ConfigureAwait(false);

        return $"{_options.Name}:{purpose}:{userId}";
    }
}
