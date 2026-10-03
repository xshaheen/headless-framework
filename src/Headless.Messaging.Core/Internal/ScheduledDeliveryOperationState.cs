// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Monitoring;

namespace Headless.Messaging.Internal;

internal readonly record struct ScheduledDeliveryOperationState(
    StatusName Status,
    int InlineAttempts,
    int Retries,
    DateTimeOffset? NextRetryAt,
    bool HasLiveLease,
    string ConfiguredVersion,
    string MessageVersion,
    DateTimeOffset? DueAt
);
