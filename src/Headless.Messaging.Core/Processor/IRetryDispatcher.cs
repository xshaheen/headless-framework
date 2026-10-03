// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;

namespace Headless.Messaging.Processor;

internal interface IRetryDispatcher
{
    ValueTask DispatchPublishedAsync(MediumMessage message, CancellationToken cancellationToken = default);

    ValueTask<bool> DispatchReceivedAsync(MediumMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches a claimed received retry. <paramref name="onAbandonedBeforeExecution"/> runs exactly
    /// once if the attempt is abandoned before execution starts (refused, drained during quiesce, or
    /// cancelled while queued); it never runs once the executor has taken ownership of the attempt.
    /// </summary>
    ValueTask<bool> DispatchReceivedAsync(
        MediumMessage message,
        Action? onAbandonedBeforeExecution,
        CancellationToken cancellationToken = default
    );
}
