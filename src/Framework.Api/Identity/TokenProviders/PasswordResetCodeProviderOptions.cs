// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Api.Identity.TokenProviders;

public sealed class PasswordResetCodeProviderOptions
{
    public const string DefaultName = "PasswordReset";

    public string Name { get; set; } = DefaultName;
}
