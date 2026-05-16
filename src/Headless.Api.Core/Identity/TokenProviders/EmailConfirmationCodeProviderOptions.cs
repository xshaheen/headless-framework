// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Identity.TokenProviders;

public sealed class EmailConfirmationCodeProviderOptions : TotpTokenProviderOptions
{
    public new const string DefaultName = "EmailConfirmation";

    public EmailConfirmationCodeProviderOptions()
    {
        Name = DefaultName;
    }
}
