// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Retry;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

internal interface IMessageSender
{
    /// <summary>
    /// Publishes a single outbox message using the sender's root service provider for
    /// scoped service resolution. Prefer <see cref="SendAsync(MediumMessage, IServiceProvider)"/>
    /// to surface the live per-message dispatch scope to the exhausted callback.
    /// </summary>
    Task<OperateResult> SendAsync(MediumMessage message);

    /// <summary>
    /// Publishes a single outbox message and threads the caller's per-message DI scope through
    /// to the retry pipeline so that <c>OnExhausted</c>'s <c>FailedInfo.ServiceProvider</c>
    /// reflects the SAME scope used while sending. The caller (Dispatcher) owns this scope's
    /// lifetime.
    /// </summary>
    Task<OperateResult> SendAsync(MediumMessage message, IServiceProvider dispatchServices);

    Task<OperateResult> SendRetryAsync(
        MediumMessage message,
        IServiceProvider dispatchServices,
        RetryExecutionState executionState
    );
}
