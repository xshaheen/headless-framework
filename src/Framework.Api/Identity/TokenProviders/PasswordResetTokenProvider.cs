// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Api.Identity.TokenProviders;

public sealed class PasswordResetTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<PasswordResetTokenProviderOptions> options,
    ILogger<PasswordResetTokenProvider<TUser>> logger
) : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class;
