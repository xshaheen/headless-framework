// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using FastExpressionCompiler;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

internal interface IPublishMiddlewarePipeline
{
    Task ExecuteAsync<T>(
        T? content,
        IntentType intentType,
        MessagePublishOptionsBase? options,
        TimeSpan? delayTime,
        Func<MessagePublishOptionsBase?, TimeSpan?, CancellationToken, Task> innerPublish,
        bool isTransactional = false,
        CancellationToken cancellationToken = default
    );
}

internal sealed class PublishMiddlewarePipeline(
    IServiceProvider serviceProvider,
    IMiddlewareDescriptorRegistry? descriptorRegistry = null,
    ILogger<PublishMiddlewarePipeline>? logger = null
) : IPublishMiddlewarePipeline
{
    private static readonly ConcurrentDictionary<MiddlewareDispatchKey, PublishMiddlewareInvoker> _TypedInvokers =
        new();

    private readonly IServiceProvider _serviceProvider = Argument.IsNotNull(serviceProvider);

    public async Task ExecuteAsync<T>(
        T? content,
        IntentType intentType,
        MessagePublishOptionsBase? options,
        TimeSpan? delayTime,
        Func<MessagePublishOptionsBase?, TimeSpan?, CancellationToken, Task> innerPublish,
        bool isTransactional = false,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var scope = _serviceProvider.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var context = new PublishingContext<T>(
            content,
            intentType,
            options,
            delayTime,
            isTransactional,
            cancellationToken
        );
        var middleware = _ResolveMiddleware(provider, context);
        var innerRingCompleted = false;

        Func<ValueTask> next = async () =>
        {
            await innerPublish(context.Options, context.DelayTime, context.CancellationToken).ConfigureAwait(false);
            innerRingCompleted = true;
            context.MarkCompleted();
        };

        for (var i = middleware.Length - 1; i >= 0; i--)
        {
            var current = middleware[i];
            var innerNext = next;
            next = () => _InvokeAsync(current, context, innerNext, () => innerRingCompleted);
        }

        await next().ConfigureAwait(false);
    }

    private async ValueTask _InvokeAsync(
        object middleware,
        PublishContext context,
        Func<ValueTask> innerNext,
        Func<bool> innerRingCompleted
    )
    {
        try
        {
            await _InvokeMiddlewareAsync(middleware, context, innerNext).ConfigureAwait(false);
            _MarkCompleted(context);
        }
        catch (Exception ex) when (innerRingCompleted())
        {
            logger?.PublishPostSuccessMiddlewareFailed(ex, middleware.GetType().FullName ?? middleware.GetType().Name);
            return;
        }
        catch (Exception ex) when (_ShouldRethrowOce(ex, context.CancellationToken))
        {
            throw new OperationCanceledException(context.CancellationToken);
        }

        if (context.CancellationToken.IsCancellationRequested)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static ValueTask _InvokeMiddlewareAsync(object middleware, PublishContext context, Func<ValueTask> next)
    {
        if (middleware is IPublishMiddleware<PublishContext> busMiddleware)
        {
            return busMiddleware.InvokeAsync(context, next);
        }

        var invoker = _TypedInvokers.GetOrAdd(
            new MiddlewareDispatchKey(middleware.GetType(), context.MessageType),
            static (_, state) => _CompileTypedInvoker(state.middlewareType, state.contextType),
            (middlewareType: middleware.GetType(), contextType: context.GetType())
        );

        return invoker(middleware, context, next);
    }

    private object[] _ResolveMiddleware(IServiceProvider provider, PublishContext context)
    {
        var directMiddleware = _ResolveDirectMiddleware(provider, context).ToArray();

        if (
            descriptorRegistry is not null
            && descriptorRegistry.TryGetPublishDescriptors(context.MessageType, out var descriptors)
        )
        {
            return descriptors
                .Select(descriptor => _ResolveDescriptor(provider, descriptor))
                .Where(static middleware => middleware is not null)
                .Cast<object>()
                .Concat(_GetUntrackedDirectMiddleware(directMiddleware, MiddlewareDirection.Publish))
                .ToArray();
        }

        return directMiddleware;
    }

    private static object[] _ResolveDirectMiddleware(IServiceProvider provider, PublishContext context)
    {
        var typedServiceType = typeof(IPublishMiddleware<>).MakeGenericType(context.GetType());
        var busMiddleware = provider.GetServices<IPublishMiddleware<PublishContext>>().Cast<object>();
        var typedMiddleware = provider
            .GetServices(typedServiceType)
            .Where(static middleware => middleware is not null)
            .Cast<object>();

        return busMiddleware.Concat(typedMiddleware).ToArray();
    }

    private IEnumerable<object> _GetUntrackedDirectMiddleware(
        IEnumerable<object> middleware,
        MiddlewareDirection direction
    )
    {
        var trackedTypes = descriptorRegistry!
            .Descriptors.Where(descriptor => descriptor.Direction == direction)
            .Select(descriptor => descriptor.MiddlewareType)
            .ToHashSet();

        return middleware.Where(current => !trackedTypes.Contains(current.GetType()));
    }

    private static object? _ResolveDescriptor(IServiceProvider provider, MiddlewareDescriptor descriptor)
    {
        return provider
            .GetServices(descriptor.ServiceType)
            .FirstOrDefault(service => service?.GetType() == descriptor.MiddlewareType);
    }

    private static PublishMiddlewareInvoker _CompileTypedInvoker(Type middlewareType, Type contextType)
    {
        var middlewareParam = Expression.Parameter(typeof(object), "middleware");
        var contextParam = Expression.Parameter(typeof(PublishContext), "context");
        var nextParam = Expression.Parameter(typeof(Func<ValueTask>), "next");
        var serviceType = typeof(IPublishMiddleware<>).MakeGenericType(contextType);
        var invokeMethod = serviceType.GetMethod(nameof(IPublishMiddleware<PublishContext>.InvokeAsync))!;

        var body = Expression.Call(
            Expression.Convert(middlewareParam, serviceType),
            invokeMethod,
            Expression.Convert(contextParam, contextType),
            nextParam
        );

        return Expression
            .Lambda<PublishMiddlewareInvoker>(body, middlewareParam, contextParam, nextParam)
            .CompileFast();
    }

    private static bool _ShouldRethrowOce(Exception exception, CancellationToken cancellationToken)
    {
        if (
            exception is OperationCanceledException operationCanceledException
            && operationCanceledException.CancellationToken == cancellationToken
        )
        {
            return true;
        }

        if (exception is AggregateException aggregateException)
        {
            return aggregateException.InnerExceptions.Any(inner => _ShouldRethrowOce(inner, cancellationToken));
        }

        return false;
    }

    private static void _MarkCompleted(PublishContext context)
    {
        if (context is ICompletablePublishContext completableContext)
        {
            completableContext.MarkCompleted();
        }
    }

    private delegate ValueTask PublishMiddlewareInvoker(
        object middleware,
        PublishContext context,
        Func<ValueTask> next
    );
}
