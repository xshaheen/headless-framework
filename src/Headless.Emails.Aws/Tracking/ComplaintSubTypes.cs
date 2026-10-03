// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>The known values of <see cref="SesComplaintEvent.ComplaintSubType"/>.</summary>
[PublicAPI]
public static class ComplaintSubTypes
{
    /// <summary>SES accepted the message but suppressed sending because the address is on the account-level suppression list.</summary>
    public const string OnAccountSuppressionList = "OnAccountSuppressionList";

    /// <summary>SES accepted the message but suppressed sending because the address is on the tenant-level suppression list.</summary>
    public const string OnTenantSuppressionList = "OnTenantSuppressionList";
}
