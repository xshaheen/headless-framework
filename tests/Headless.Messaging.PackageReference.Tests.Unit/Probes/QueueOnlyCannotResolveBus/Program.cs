// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;

namespace Tests.Probes;

internal sealed class QueueOnlyCannotResolveBus
{
    public IQueue Queue { get; init; } = null!;

    public IBus Bus { get; init; } = null!;
}
