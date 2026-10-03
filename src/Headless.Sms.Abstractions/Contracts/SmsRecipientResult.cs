// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Sms;

/// <summary>The outcome for one recipient within a <see cref="SendBulkSmsResponse"/>.</summary>
/// <param name="Destination">The recipient this outcome belongs to.</param>
/// <param name="Result">The single-send outcome for this recipient.</param>
[PublicAPI]
public sealed record SmsRecipientResult(SmsRequestDestination Destination, SendSingleSmsResponse Result);
