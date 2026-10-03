// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Retry;

/// <summary>
/// Value returned by the <c>RetryHelper.ResolveNextState</c> overloads describing the persistence state
/// for a single failed delivery attempt.
/// </summary>
internal readonly record struct RetryNextState(
    bool IsInlineRetryInFlight,
    RetryDelay? NextRetry,
    StatusName NextStatus
);
