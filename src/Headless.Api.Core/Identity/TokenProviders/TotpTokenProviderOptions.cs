// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Identity.TokenProviders;

/// <summary>Configuration options for <see cref="TotpTokenProvider{TUser}"/>.</summary>
[PublicAPI]
public class TotpTokenProviderOptions
{
    /// <summary>Default provider name (<c>"Totp"</c>).</summary>
    public const string DefaultName = "Totp";

    /// <summary>
    /// Provider name, used as part of the TOTP modifier string and as the Identity token provider name.
    /// Defaults to <see cref="DefaultName"/>.
    /// </summary>
    public string Name { get; set; } = DefaultName;

    /// <summary>
    /// Duration of each TOTP time step. Codes are valid for this duration (plus the variance window).
    /// Defaults to 3 minutes.
    /// </summary>
    public TimeSpan Timestep { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>
    /// Number of adjacent time steps (before and after the current step) that are accepted during validation.
    /// A value of 2 means codes up to 2 steps old or 2 steps in the future are accepted.
    /// Defaults to 2.
    /// </summary>
    public int Variance { get; set; } = 2;

    /// <summary>HMAC algorithm used to compute TOTP codes. Defaults to <see cref="TotpHashMode.Sha1"/>.</summary>
    public TotpHashMode HashMode { get; set; } = TotpHashMode.Sha1;
}
