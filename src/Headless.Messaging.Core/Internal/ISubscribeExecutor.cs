// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging.Internal;

/// <summary>
/// Consumer executor
/// </summary>
public interface ISubscribeExecutor
{
    Task<OperateResult> ExecuteAsync(
        MediumMessage message,
        ConsumerExecutorDescriptor? descriptor = null,
        CancellationToken cancellationToken = default
    );
}
