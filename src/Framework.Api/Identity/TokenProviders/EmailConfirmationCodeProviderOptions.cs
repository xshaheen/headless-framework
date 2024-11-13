// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Api.Identity.TokenProviders;

public sealed class EmailConfirmationCodeProviderOptions
{
    public const string DefaultName = "EmailConfirmation";

    public string Name { get; set; } = DefaultName;
}
