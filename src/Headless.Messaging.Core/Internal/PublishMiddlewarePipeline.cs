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
        PublishOptions? options,
        TimeSpan? delayTime,
        Func<PublishOptions?, TimeSpan?, CancellationToken, Task> innerPublish,
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
        PublishOptions? options,
        TimeSpan? delayTime,
        Func<PublishOptions?, TimeSpan?, CancellationToken, Task> innerPublish,
        bool isTransactional = false,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var scope = _serviceProvider.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var context = new PublishingContext<T>(content, options, delayTime, isTransactional, cancellationToken);
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

            if (context.CancellationToken.IsCancellationRequested)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (Exception ex) when (_ShouldRethrowOce(ex, context.CancellationToken))
        {
            if (ex is AggregateException)
            {
                throw;
            }

            throw new OperationCanceledException(context.CancellationToken);
        }
        catch (Exception ex) when (innerRingCompleted())
        {
            logger?.PublishPostSuccessMiddlewareFailed(ex, middleware.GetType().FullName ?? middleware.GetType().Name);
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
        var descriptors = descriptorRegistry?.GetPublishDescriptors(context.MessageType) ?? [];

        if (descriptors.Count > 0)
        {
            return descriptors
                .Select(descriptor => _ResolveDescriptor(provider, descriptor))
                .Where(static middleware => middleware is not null)
                .Cast<object>()
                .ToArray();
        }

        var typedServiceType = typeof(IPublishMiddleware<>).MakeGenericType(context.GetType());
        var busMiddleware = provider.GetServices<IPublishMiddleware<PublishContext>>().Cast<object>();
        var typedMiddleware = provider
            .GetServices(typedServiceType)
            .Where(static middleware => middleware is not null)
            .Cast<object>();

        return busMiddleware.Concat(typedMiddleware).ToArray();
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

    private delegate ValueTask PublishMiddlewareInvoker(
        object middleware,
        PublishContext context,
        Func<ValueTask> next
    );
}
