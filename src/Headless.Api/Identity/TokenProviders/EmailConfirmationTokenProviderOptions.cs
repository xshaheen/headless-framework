// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Identity;

namespace Headless.Api.Identity.TokenProviders;

public sealed class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public const string DefaultName = "EmailConfirmation";

    public EmailConfirmationTokenProviderOptions()
    {
        Name = DefaultName;
        TokenLifespan = TimeSpan.FromHours(6);
    }
}
