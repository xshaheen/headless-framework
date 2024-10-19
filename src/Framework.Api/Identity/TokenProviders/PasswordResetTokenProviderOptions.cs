// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.AspNetCore.Identity;

namespace Framework.Api.Identity.TokenProviders;

public sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public const string DefaultName = "PasswordReset";

    public PasswordResetTokenProviderOptions()
    {
        Name = DefaultName;
        TokenLifespan = TimeSpan.FromHours(6);
    }
}
