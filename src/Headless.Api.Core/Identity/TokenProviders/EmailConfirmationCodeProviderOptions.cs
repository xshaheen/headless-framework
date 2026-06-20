// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Identity.TokenProviders;

/// <summary>
/// Options for <see cref="EmailConfirmationCodeProvider{TUser}"/>.
/// Inherits all TOTP settings from <see cref="TotpTokenProviderOptions"/> and sets the
/// default provider name to <c>"EmailConfirmation"</c>.
/// </summary>
[PublicAPI]
public sealed class EmailConfirmationCodeProviderOptions : TotpTokenProviderOptions
{
    /// <summary>Default provider name (<c>"EmailConfirmation"</c>).</summary>
    public new const string DefaultName = "EmailConfirmation";

    /// <summary>Initializes options with the default name set to <see cref="DefaultName"/>.</summary>
    public EmailConfirmationCodeProviderOptions()
    {
        Name = DefaultName;
    }
}
