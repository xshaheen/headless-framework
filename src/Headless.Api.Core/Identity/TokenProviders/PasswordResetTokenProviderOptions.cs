// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Identity;

namespace Headless.Api.Identity.TokenProviders;

/// <summary>
/// Options for <see cref="PasswordResetTokenProvider{TUser}"/>.
/// Sets default name to <c>"PasswordReset"</c> and token lifetime to 6 hours.
/// </summary>
[PublicAPI]
public sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions
{
    /// <summary>Default provider name (<c>"PasswordReset"</c>).</summary>
    public const string DefaultName = "PasswordReset";

    /// <summary>
    /// Initializes options with <see cref="DefaultName"/> and a 6-hour token lifetime.
    /// </summary>
    public PasswordResetTokenProviderOptions()
    {
        Name = DefaultName;
        TokenLifespan = TimeSpan.FromHours(6);
    }
}
