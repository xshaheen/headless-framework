// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using FastExpressionCompiler;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

internal interface IPublishMiddlewarePipeline
{
    Task ExecuteAsync(
        object? content,
        Type declaredMessageType,
        MessageLane lane,
        MessageOptions? options,
        DeliveryDecision decision,
        Func<MessageOptions?, CancellationToken, Task> innerPublish,
        CancellationToken cancellationToken = default
    );

    Task ExecuteAsync<T>(
        T? content,
        MessageLane lane,
        MessageOptions? options,
        DeliveryDecision decision,
        Func<MessageOptions?, CancellationToken, Task> innerPublish,
        CancellationToken cancellationToken = default
    );
}
