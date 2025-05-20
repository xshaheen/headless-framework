// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Api.Identity.TokenProviders;

public class TotpTokenProviderOptions
{
    public const string DefaultName = "Totp";

    public string Name { get; set; } = DefaultName;
}
