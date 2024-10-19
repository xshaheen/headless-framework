// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Api.Identity.TokenProviders;

public sealed class EmailConfirmationTokenProvider<TUser>(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<EmailConfirmationTokenProviderOptions> options,
    ILogger<EmailConfirmationTokenProvider<TUser>> logger
) : DataProtectorTokenProvider<TUser>(dataProtectionProvider, options, logger)
    where TUser : class;
