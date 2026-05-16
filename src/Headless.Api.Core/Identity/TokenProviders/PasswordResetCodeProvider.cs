// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;

namespace Headless.Api.Identity.TokenProviders;

/// <summary>
/// Generates and validates 6-digit TOTP codes for password reset with configurable
/// timestep, variance, and hash algorithm.
/// </summary>
public sealed class PasswordResetCodeProvider<TUser>(
    TotpRfc6238Generator generator,
    IOptions<PasswordResetCodeProviderOptions> optionsAccessor
) : TotpTokenProvider<TUser>(generator, optionsAccessor.Value)
    where TUser : class;
