// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>
/// The known values of <see cref="SesComplaintEvent.ComplaintFeedbackType"/>, as assigned by the reporting
/// ISP per the IANA MARF parameters registry.
/// </summary>
[PublicAPI]
public static class ComplaintFeedbackTypes
{
    /// <summary>Unsolicited email or some other kind of email abuse.</summary>
    public const string Abuse = "abuse";

    /// <summary>Email authentication failure report.</summary>
    public const string AuthFailure = "auth-failure";

    /// <summary>Some kind of fraud or phishing activity.</summary>
    public const string Fraud = "fraud";

    /// <summary>The reporting entity does not consider the message to be spam.</summary>
    public const string NotSpam = "not-spam";

    /// <summary>A virus was found in the originating message.</summary>
    public const string Virus = "virus";

    /// <summary>Any other feedback that does not fit a registered type.</summary>
    public const string Other = "other";
}
