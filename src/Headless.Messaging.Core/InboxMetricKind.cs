// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Monitoring;

namespace Headless.Messaging;

internal enum InboxMetricKind
{
    Duplicate = 0,
    Attempt = 1,
    Recovery = 2,
    Terminal = 3,
    Replay = 4,
    Retention = 5,
    Capability = 6,
}
