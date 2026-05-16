// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Api.Identity.TokenProviders;

public sealed class PasswordResetTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<PasswordResetTokenProviderOptions> optionsAccessor,
    ILogger<PasswordResetTokenProvider<TUser>> logger
) : DataProtectorTokenProvider<TUser>(dataProtectionProvider, optionsAccessor, logger)
    where TUser : class;
