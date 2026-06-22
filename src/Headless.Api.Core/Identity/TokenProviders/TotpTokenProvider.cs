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
[PublicAPI]
public class TotpTokenProvider<TUser> : IUserTwoFactorTokenProvider<TUser>
    where TUser : class
{
    private readonly TotpRfc6238Generator _generator;
    private readonly TotpTokenProviderOptions _options;

    /// <summary>
    /// Initializes a new <see cref="TotpTokenProvider{TUser}"/> using options from the DI container.
    /// </summary>
    /// <param name="generator">The RFC 6238 TOTP generator service.</param>
    /// <param name="optionsAccessor">Options for this provider.</param>
    public TotpTokenProvider(TotpRfc6238Generator generator, IOptions<TotpTokenProviderOptions> optionsAccessor)
        : this(generator, optionsAccessor.Value) { }

    /// <summary>
    /// Initializes a new <see cref="TotpTokenProvider{TUser}"/> with an explicit options instance.
    /// Used by subclasses that bind a derived options type.
    /// </summary>
    /// <param name="generator">The RFC 6238 TOTP generator service.</param>
    /// <param name="options">Provider options (timestep, variance, hash mode, name).</param>
    protected TotpTokenProvider(TotpRfc6238Generator generator, TotpTokenProviderOptions options)
    {
        _generator = generator;
        _options = options;
    }

    /// <summary>Always returns <see langword="false"/>; TOTP codes are not used as 2FA tokens in this provider.</summary>
    public Task<bool> CanGenerateTwoFactorTokenAsync(UserManager<TUser> manager, TUser user) => Task.FromResult(false);

    /// <summary>Generates a 6-digit TOTP code bound to <paramref name="user"/> and <paramref name="purpose"/>.</summary>
    /// <param name="purpose">The intended purpose of the token (e.g. <c>"EmailConfirmation"</c>).</param>
    /// <param name="manager">The <see cref="UserManager{TUser}"/> used to retrieve the user's security token and ID.</param>
    /// <param name="user">The user for whom the code is generated.</param>
    /// <returns>A zero-padded 6-digit TOTP code string.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="manager"/> is <see langword="null"/>.</exception>
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

    /// <summary>Validates a TOTP <paramref name="token"/> for the given <paramref name="user"/> and <paramref name="purpose"/>.</summary>
    /// <param name="purpose">The purpose the token was generated for.</param>
    /// <param name="token">The 6-digit code to validate.</param>
    /// <param name="manager">The <see cref="UserManager{TUser}"/> used to retrieve the user's security token and ID.</param>
    /// <param name="user">The user whose TOTP is being validated.</param>
    /// <returns>
    /// <see langword="true"/> if the code is valid within the configured timestep variance;
    /// <see langword="false"/> if the code is invalid, expired, or non-numeric.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="manager"/> is <see langword="null"/>.</exception>
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
    /// Default format: <c>{Name}:{purpose}:{userId}</c>.
    /// Override in a derived class to change the binding (e.g. bind to the user's email instead of ID).
    /// </summary>
    /// <param name="purpose">The intended purpose (e.g. <c>"EmailConfirmation"</c>).</param>
    /// <param name="manager">The <see cref="UserManager{TUser}"/> used to retrieve the user ID.</param>
    /// <param name="user">The user for whom the modifier is built.</param>
    /// <returns>A string that uniquely identifies the user-purpose combination.</returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="purpose"/>, <paramref name="manager"/>, or <paramref name="user"/> is <see langword="null"/>.
    /// </exception>
    protected virtual async Task<string> GetUserModifierAsync(string purpose, UserManager<TUser> manager, TUser user)
    {
        Argument.IsNotNull(purpose);
        Argument.IsNotNull(manager);
        Argument.IsNotNull(user);

        var userId = await manager.GetUserIdAsync(user).ConfigureAwait(false);

        return $"{_options.Name}:{purpose}:{userId}";
    }
}
