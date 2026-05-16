// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Identity.TokenProviders;

public sealed class PasswordResetCodeProviderOptions : TotpTokenProviderOptions
{
    public new const string DefaultName = "PasswordReset";

    public PasswordResetCodeProviderOptions()
    {
        Name = DefaultName;
    }
}
