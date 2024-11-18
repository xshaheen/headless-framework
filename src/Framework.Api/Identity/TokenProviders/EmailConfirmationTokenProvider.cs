// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Api.Identity.TokenProviders;

public sealed class EmailConfirmationTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<EmailConfirmationTokenProviderOptions> optionsAccessor,
    ILogger<EmailConfirmationTokenProvider<TUser>> logger
) : DataProtectorTokenProvider<TUser>(dataProtectionProvider, optionsAccessor, logger)
    where TUser : class;
