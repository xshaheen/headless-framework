// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Sms.Connekio;

/// <summary>Options for the Connekio SMS provider.</summary>
/// <remarks>
/// Connekio uses HTTP Basic authentication composed as <c>{UserName}:{Password}:{AccountId}</c>. Single
/// and batch sends route to separate endpoints; the sender selects the endpoint automatically based on the
/// number of recipients in the request.
/// </remarks>
public sealed class ConnekioSmsOptions
{
    /// <summary>The Connekio endpoint for sending a single SMS. Defaults to the Connekio production URL.</summary>
    public string SingleSmsEndpoint { get; set; } = "https://api.connekio.com/sms/single";

    /// <summary>The Connekio endpoint for sending a batch SMS to multiple recipients. Defaults to the Connekio production URL.</summary>
    public string BatchSmsEndpoint { get; set; } = "https://api.connekio.com/sms/batch";

    /// <summary>The registered sender name or number displayed to recipients.</summary>
    public required string Sender { get; set; }

    /// <summary>The Connekio account identifier, included in the Basic auth credential string.</summary>
    public required string AccountId { get; set; }

    /// <summary>The Connekio account username used for Basic authentication.</summary>
    public required string UserName { get; set; }

    /// <summary>The Connekio account password used for Basic authentication.</summary>
    public required string Password { get; set; }
}

[UsedImplicitly]
internal sealed class ConnekioSmsOptionsValidator : AbstractValidator<ConnekioSmsOptions>
{
    public ConnekioSmsOptionsValidator()
    {
        RuleFor(x => x.SingleSmsEndpoint).NotEmpty().HttpsOnlyUrl();
        RuleFor(x => x.BatchSmsEndpoint).NotEmpty().HttpsOnlyUrl();
        RuleFor(x => x.Sender).NotEmpty();
        RuleFor(x => x.AccountId).NotEmpty();
        RuleFor(x => x.UserName).NotEmpty();
        RuleFor(x => x.Password).NotEmpty();
    }
}
