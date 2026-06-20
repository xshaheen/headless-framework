// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Identity;

namespace Headless.Api.Identity.TokenProviders;

/// <summary>
/// Options for <see cref="EmailConfirmationTokenProvider{TUser}"/>.
/// Sets default name to <c>"EmailConfirmation"</c> and token lifetime to 6 hours.
/// </summary>
[PublicAPI]
public sealed class EmailConfirmationTokenProviderOptions : DataProtectionTokenProviderOptions
{
    /// <summary>Default provider name (<c>"EmailConfirmation"</c>).</summary>
    public const string DefaultName = "EmailConfirmation";

    /// <summary>
    /// Initializes options with <see cref="DefaultName"/> and a 6-hour token lifetime.
    /// </summary>
    public EmailConfirmationTokenProviderOptions()
    {
        Name = DefaultName;
        TokenLifespan = TimeSpan.FromHours(6);
    }
}
