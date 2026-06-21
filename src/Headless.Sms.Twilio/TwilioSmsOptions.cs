// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Sms.Twilio;

/// <summary>Options for the Twilio SMS provider.</summary>
/// <remarks>
/// <para>
/// Twilio does not support multi-destination batch sends through a single API call; requests with more
/// than one destination are rejected with a failed response.
/// </para>
/// <para>
/// Credentials (<see cref="Sid"/> / <see cref="AuthToken"/>) are used as Basic auth username/password
/// against the Twilio REST API.
/// </para>
/// </remarks>
public sealed class TwilioSmsOptions
{
    /// <summary>The Twilio Account SID, used as the API username. Found in the Twilio Console dashboard.</summary>
    public required string Sid { get; init; }

    /// <summary>The Twilio Auth Token, used as the API password. Found in the Twilio Console dashboard.</summary>
    public required string AuthToken { get; init; }

    /// <summary>The Twilio phone number to send from, in E.164 format (for example <c>+12025551234</c>).</summary>
    public required string PhoneNumber { get; init; }

    /// <summary>
    /// Optional maximum price (in USD) Twilio will charge per message. When specified, Twilio will not
    /// send the message if the carrier cost exceeds this threshold. <see langword="null"/> applies no limit.
    /// </summary>
    public decimal? MaxPrice { get; init; }

    /// <summary>
    /// Optional Twilio region (for example <c>us1</c>, <c>ie1</c>). Controls which Twilio infrastructure
    /// region handles the request. <see langword="null"/> uses the default region.
    /// </summary>
    public string? Region { get; init; }

    /// <summary>
    /// Optional Twilio Edge location (for example <c>ashburn</c>, <c>dublin</c>). Combined with
    /// <see cref="Region"/> to select a specific point of presence. <see langword="null"/> uses the
    /// default edge.
    /// </summary>
    public string? Edge { get; init; }
}

[UsedImplicitly]
internal sealed class TwilioSmsOptionsValidator : AbstractValidator<TwilioSmsOptions>
{
    public TwilioSmsOptionsValidator()
    {
        RuleFor(x => x.Sid).NotEmpty();
        RuleFor(x => x.AuthToken).NotEmpty();
        RuleFor(x => x.PhoneNumber).NotEmpty().InternationalPhoneNumber();
        RuleFor(x => x.MaxPrice).GreaterThanOrEqualTo(0);
    }
}
