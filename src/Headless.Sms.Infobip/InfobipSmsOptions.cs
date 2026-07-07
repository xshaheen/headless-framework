// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Sms.Infobip;

/// <summary>Options for the Infobip SMS provider.</summary>
/// <remarks>
/// The Infobip SDK authenticates using the <c>App-</c> API key scheme. <see cref="BasePath"/> is the
/// personal base URL shown on the Infobip web portal (for example
/// <c>https://&lt;your-id&gt;.api.infobip.com</c>).
/// </remarks>
public sealed class InfobipSmsOptions
{
    /// <summary>The registered sender name or number shown to recipients.</summary>
    public required string Sender { get; set; }

    /// <summary>The Infobip API key used for authentication (passed as the <c>Authorization: App {ApiKey}</c> header by the SDK).</summary>
    public required string ApiKey { get; set; }

    /// <summary>
    /// The personal base URL for the Infobip account, for example
    /// <c>https://&lt;your-id&gt;.api.infobip.com</c>. Found on the Infobip web portal under API keys.
    /// </summary>
    public required string BasePath { get; set; }
}

[UsedImplicitly]
internal sealed class InfobipSmsOptionsValidator : AbstractValidator<InfobipSmsOptions>
{
    public InfobipSmsOptionsValidator()
    {
        RuleFor(x => x.Sender).NotEmpty();
        RuleFor(x => x.ApiKey).NotEmpty();
        RuleFor(x => x.BasePath).NotEmpty().HttpsOnlyUrl();
    }
}
