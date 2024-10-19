// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.AspNetCore.Identity;

namespace Framework.Api.Identity.TokenProviders;

public sealed class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public const string DefaultName = "EmailConfirmation";

    public EmailConfirmationTokenProviderOptions()
    {
        Name = DefaultName;
        TokenLifespan = TimeSpan.FromHours(6);
    }
}
