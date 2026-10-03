// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Jobs.Models;

namespace Headless.Jobs;

internal enum JobsExecutionCancellationCause
{
    None,
    DurableCancellation,
    HostShutdown,
    LeaseLost,
}
