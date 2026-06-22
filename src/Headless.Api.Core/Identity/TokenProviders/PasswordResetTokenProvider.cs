// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Api.Identity.TokenProviders;

/// <summary>
/// Data-protection-based token provider for password reset links.
/// Wraps <see cref="DataProtectorTokenProvider{TUser}"/> with the
/// <see cref="PasswordResetTokenProviderOptions"/> defaults (6-hour lifetime).
/// Produces opaque tokens suitable for URL-safe password reset links.
/// </summary>
/// <typeparam name="TUser">The user type managed by ASP.NET Core Identity.</typeparam>
public sealed class PasswordResetTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<PasswordResetTokenProviderOptions> optionsAccessor,
    ILogger<PasswordResetTokenProvider<TUser>> logger
) : DataProtectorTokenProvider<TUser>(dataProtectionProvider, optionsAccessor, logger)
    where TUser : class;
