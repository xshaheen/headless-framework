// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Emails.Aws.Tracking;

/// <summary>
/// Information about a <c>Send</c> event. The object SES publishes for a send event is always empty;
/// its presence on <see cref="EmailTrackingNotification"/> indicates the event type.
/// </summary>
[PublicAPI]
public sealed record SendEvent;
