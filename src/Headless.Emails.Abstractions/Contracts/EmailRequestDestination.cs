// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails;

/// <summary>
/// The set of recipient addresses for an outgoing email.
/// </summary>
[PublicAPI]
public sealed record EmailRequestDestination
{
    /// <summary>The primary recipients (To line).</summary>
    public required IReadOnlyList<EmailRequestAddress> ToAddresses { get; init; }

    /// <summary>Blind-carbon-copy recipients. Defaults to an empty list.</summary>
    public IReadOnlyList<EmailRequestAddress> BccAddresses { get; init; } = [];

    /// <summary>Carbon-copy recipients. Defaults to an empty list.</summary>
    public IReadOnlyList<EmailRequestAddress> CcAddresses { get; init; } = [];
}
