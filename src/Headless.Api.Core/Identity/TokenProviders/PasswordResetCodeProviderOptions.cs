// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Identity.TokenProviders;

/// <summary>
/// Options for <see cref="PasswordResetCodeProvider{TUser}"/>.
/// Inherits all TOTP settings from <see cref="TotpTokenProviderOptions"/> and sets the
/// default provider name to <c>"PasswordReset"</c>.
/// </summary>
[PublicAPI]
public sealed class PasswordResetCodeProviderOptions : TotpTokenProviderOptions
{
    /// <summary>Default provider name (<c>"PasswordReset"</c>).</summary>
    public new const string DefaultName = "PasswordReset";

    /// <summary>Initializes options with the default name set to <see cref="DefaultName"/>.</summary>
    public PasswordResetCodeProviderOptions()
    {
        Name = DefaultName;
    }
}
