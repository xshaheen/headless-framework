// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

namespace Framework.Api.Identity.TokenProviders;

public sealed class EmailConfirmationCodeProviderOptions
{
    public const string DefaultName = "EmailConfirmation";

    public string Name { get; set; } = DefaultName;
}
