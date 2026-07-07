// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Sms.VictoryLink;

/// <summary>Options for the VictoryLink SMS provider.</summary>
/// <remarks>
/// VictoryLink auto-detects whether the message text is Arabic (RTL) and sets the language code
/// accordingly. Credentials (<see cref="UserName"/> / <see cref="Password"/>) are sent in the request
/// body on every call.
/// </remarks>
public sealed class VictoryLinkSmsOptions
{
    /// <summary>The VictoryLink API endpoint for sending SMS messages. Defaults to the VictoryLink production URL.</summary>
    public string Endpoint { get; set; } =
        "https://smsvas.vlserv.com/VLSMSPlatformResellerAPI/NewSendingAPI/api/SMSSender/SendSMS";

    /// <summary>The registered sender name displayed to recipients (the <c>SMSSender</c> field).</summary>
    public required string Sender { get; set; }

    /// <summary>The VictoryLink account username sent with every request for authentication.</summary>
    public required string UserName { get; set; }

    /// <summary>The VictoryLink account password sent with every request for authentication.</summary>
    public required string Password { get; set; }
}

[UsedImplicitly]
internal sealed class VictoryLinkSmsOptionsValidator : AbstractValidator<VictoryLinkSmsOptions>
{
    public VictoryLinkSmsOptionsValidator()
    {
        RuleFor(x => x.Endpoint).NotEmpty().HttpsOnlyUrl();
        RuleFor(x => x.Sender).NotEmpty();
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}
