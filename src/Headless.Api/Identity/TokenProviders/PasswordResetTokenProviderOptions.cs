// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Identity;

namespace Headless.Api.Identity.TokenProviders;

public sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public const string DefaultName = "PasswordReset";

    public PasswordResetTokenProviderOptions()
    {
        Name = DefaultName;
        TokenLifespan = TimeSpan.FromHours(6);
    }
}
