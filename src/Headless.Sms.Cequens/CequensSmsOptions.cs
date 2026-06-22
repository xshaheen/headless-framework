// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Sms.Cequens;

/// <summary>Options for the Cequens SMS provider.</summary>
/// <remarks>
/// The sender authenticates using <see cref="ApiKey"/> and <see cref="UserName"/> to obtain a short-lived
/// JWT from <see cref="TokenEndpoint"/>. The token is cached and renewed automatically on expiry or on a
/// 401 response. Supply <see cref="Token"/> to bypass dynamic token acquisition and use a static token
/// instead (useful for testing or environments where outbound auth calls are restricted).
/// </remarks>
public sealed class CequensSmsOptions
{
    /// <summary>The Cequens REST endpoint for sending a single SMS. Defaults to the Cequens production URL.</summary>
    public string SingleSmsEndpoint { get; init; } = "https://apis.cequens.com/sms/v1/messages";

    /// <summary>The Cequens authentication endpoint used to exchange credentials for a JWT. Defaults to the Cequens production URL.</summary>
    public string TokenEndpoint { get; init; } = "https://apis.cequens.com/auth/v1/tokens";

    /// <summary>The Cequens API key used together with <see cref="UserName"/> to authenticate and obtain a JWT.</summary>
    public required string ApiKey { get; init; }

    /// <summary>The Cequens account username used together with <see cref="ApiKey"/> to authenticate and obtain a JWT.</summary>
    public required string UserName { get; init; }

    /// <summary>The registered sender name displayed to the SMS recipient.</summary>
    public required string SenderName { get; init; }

    /// <summary>
    /// Optional static JWT to use instead of dynamically fetching one from <see cref="TokenEndpoint"/>.
    /// When set, the sender skips the sign-in call and uses this token directly. May be
    /// <see langword="null"/> to enable dynamic token management.
    /// </summary>
    public string? Token { get; init; }
}

[UsedImplicitly]
internal sealed class CequensSmsOptionsValidator : AbstractValidator<CequensSmsOptions>
{
    public CequensSmsOptionsValidator()
    {
        RuleFor(x => x.SingleSmsEndpoint).NotEmpty().HttpsOnlyUrl();
        RuleFor(x => x.TokenEndpoint).NotEmpty().HttpsOnlyUrl();
        RuleFor(x => x.ApiKey).NotEmpty();
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.SenderName).NotEmpty();
    }
}
