// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Messages;
using Framework.Messages.Monitoring;

namespace Framework.Messages.Internal;

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
