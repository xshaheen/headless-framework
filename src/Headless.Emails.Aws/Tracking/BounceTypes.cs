// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>The possible values of <see cref="SesBounceEvent.BounceType"/>.</summary>
[PublicAPI]
public static class BounceTypes
{
    /// <summary>SES was unable to determine a specific bounce reason.</summary>
    public const string Undetermined = "Undetermined";

    /// <summary>A hard bounce. Remove the recipient from your mailing list.</summary>
    public const string Permanent = "Permanent";

    /// <summary>A soft bounce that SES has stopped retrying. A later send may succeed.</summary>
    public const string Transient = "Transient";
}
