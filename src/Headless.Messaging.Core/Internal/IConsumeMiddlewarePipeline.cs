// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using FastExpressionCompiler;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.MultiTenancy;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

internal interface IConsumeMiddlewarePipeline
{
    Task<ConsumerExecutedResult> ExecuteInScopeAsync(
        ConsumerContext context,
        object messageInstance,
        Type messageType,
        IServiceProvider provider,
        CancellationToken cancellationToken = default
    );

    Task<ConsumerExecutedResult> ExecuteAsync(
        ConsumerContext context,
        object messageInstance,
        Type messageType,
        CancellationToken cancellationToken = default
    );
}
