// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using FastExpressionCompiler;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

internal interface IConsumeMiddlewarePipeline
{
    Task<ConsumerExecutedResult> ExecuteAsync(
        ConsumerContext context,
        object messageInstance,
        Type messageType,
        CancellationToken cancellationToken = default
    );
}

internal sealed class ConsumeMiddlewarePipeline(
    IServiceProvider serviceProvider,
    IRuntimeConsumerRegistry runtimeRegistry,
    IMiddlewareDescriptorRegistry? descriptorRegistry = null,
    ILogger<ConsumeMiddlewarePipeline>? logger = null
) : IConsumeMiddlewarePipeline
{
    private static readonly ConcurrentDictionary<MiddlewareDispatchKey, ConsumeMiddlewareInvoker> _TypedInvokers =
        new();

    private readonly ConditionalWeakTable<Type, Delegate> _compiledConsumeContextFactories = new();

    private static readonly ConditionalWeakTable<Type, Delegate>.CreateValueCallback _CompileFactoryCallback =
        _CompileFactory;

    public async Task<ConsumerExecutedResult> ExecuteAsync(
        ConsumerContext context,
        object messageInstance,
        Type messageType,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var descriptor = context.ConsumerDescriptor;
        var originHeaders = context.MediumMessage.Origin.Headers;
        var tenantId = TenantContextScope.ResolveTenantId(originHeaders, logger);
        var consumeHeaders = new MessageHeader(originHeaders);
        var consumeContext = _BuildConsumeContext(
            messageInstance,
            context.MediumMessage,
            messageType,
            consumeHeaders,
            tenantId,
            cancellationToken
        );

        await using var scope = serviceProvider.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var middleware = _ResolveMiddleware(provider, consumeContext, descriptor.GroupName);
        object? resultObj = null;
        var innerRingCompleted = false;

        Func<ValueTask> next = async () =>
        {
            if (
                descriptor.HandlerId is { Length: > 0 } handlerId
                && runtimeRegistry.TryGetInvoker(
                    descriptor.TopicName,
                    descriptor.GroupName,
                    handlerId,
                    out var runtimeInvoker
                )
            )
            {
                await runtimeInvoker
                    .InvokeAsync(consumeContext, provider, consumeContext.CancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                var dispatcher = provider.GetRequiredService<IMessageDispatcher>();
                await _DispatchAsync(
                        dispatcher,
                        provider,
                        descriptor,
                        consumeContext,
                        messageType,
                        consumeContext.CancellationToken
                    )
                    .ConfigureAwait(false);
            }

            innerRingCompleted = true;
        };

        for (var i = middleware.Length - 1; i >= 0; i--)
        {
            var current = middleware[i];
            var innerNext = next;
            next = () => _InvokeAsync(current, consumeContext, innerNext, () => innerRingCompleted);
        }

        await next().ConfigureAwait(false);

        var callbackName = context.MediumMessage.Origin.GetCallbackName();
        var callbackHeaders = consumeHeaders.ResponseHeader;
        return string.IsNullOrEmpty(callbackName)
            ? new ConsumerExecutedResult(resultObj, context.MediumMessage.Origin.GetId(), null, callbackHeaders)
            : new ConsumerExecutedResult(
                resultObj,
                context.MediumMessage.Origin.GetId(),
                callbackName,
                callbackHeaders
            );
    }

    private async ValueTask _InvokeAsync(
        object middleware,
        ConsumeContext context,
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
            logger?.ConsumePostSuccessMiddlewareFailed(ex, middleware.GetType().FullName ?? middleware.GetType().Name);
        }
    }

    private static ValueTask _InvokeMiddlewareAsync(object middleware, ConsumeContext context, Func<ValueTask> next)
    {
        if (middleware is IConsumeMiddleware<ConsumeContext> busMiddleware)
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

    private object[] _ResolveMiddleware(IServiceProvider provider, ConsumeContext context, string? groupName)
    {
        var descriptors = descriptorRegistry?.GetConsumeDescriptors(context.MessageType, groupName) ?? [];

        if (
            descriptorRegistry?.Descriptors.Any(static descriptor =>
                descriptor.Direction == MiddlewareDirection.Consume
            ) == true
        )
        {
            return descriptors
                .Select(descriptor => _ResolveDescriptor(provider, descriptor))
                .Where(static middleware => middleware is not null)
                .Cast<object>()
                .ToArray();
        }

        var typedServiceType = typeof(IConsumeMiddleware<>).MakeGenericType(context.GetType());
        var busMiddleware = provider.GetServices<IConsumeMiddleware<ConsumeContext>>().Cast<object>();
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

    private ConsumeContext _BuildConsumeContext(
        object messageInstance,
        MediumMessage mediumMessage,
        Type messageType,
        MessageHeader headers,
        string? tenantId,
        CancellationToken cancellationToken
    )
    {
        var factory =
            (Func<object, MediumMessage, MessageHeader, string?, object>)
                _compiledConsumeContextFactories.GetValue(messageType, _CompileFactoryCallback);
        var context = (ConsumeContext)factory(messageInstance, mediumMessage, headers, tenantId);
        context.WithCancellationToken(cancellationToken);

        return context;
    }

    private static Delegate _CompileFactory(Type messageType)
    {
        var consumeContextType = typeof(ConsumeContext<>).MakeGenericType(messageType);
        var messageParam = Expression.Parameter(typeof(object), "message");
        var mediumParam = Expression.Parameter(typeof(MediumMessage), "medium");
        var consumeHeadersParam = Expression.Parameter(typeof(MessageHeader), "headers");
        var tenantIdParam = Expression.Parameter(typeof(string), "tenantId");
        var originProperty = Expression.Property(mediumParam, nameof(MediumMessage.Origin));
        var addedProperty = Expression.Property(mediumParam, nameof(MediumMessage.Added));
        var headersProperty = Expression.Property(originProperty, nameof(Message.Headers));

        var messageProperty = consumeContextType.GetProperty(
            nameof(ConsumeContext<>.Message),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;
        var messageIdProperty = consumeContextType.GetProperty(nameof(ConsumeContext<>.MessageId))!;
        var correlationIdProperty = consumeContextType.GetProperty(nameof(ConsumeContext<>.CorrelationId))!;
        var tenantIdProperty = consumeContextType.GetProperty(nameof(ConsumeContext<>.TenantId))!;
        var headersCtxProperty = consumeContextType.GetProperty(nameof(ConsumeContext<>.Headers))!;
        var timestampProperty = consumeContextType.GetProperty(nameof(ConsumeContext<>.Timestamp))!;
        var topicProperty = consumeContextType.GetProperty(nameof(ConsumeContext<>.Topic))!;

        var dictionaryIndexer = typeof(IDictionary<string, string?>).GetProperty(
            "Item",
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
        )!;

        var messageBinding = Expression.Bind(messageProperty, Expression.Convert(messageParam, messageType));

        var messageIdKey = Expression.Constant(Headers.MessageId);
        var messageIdBinding = Expression.Bind(
            messageIdProperty,
            Expression.Coalesce(
                Expression.MakeIndex(headersProperty, dictionaryIndexer, [messageIdKey]),
                Expression.Constant(string.Empty)
            )
        );

        var correlationIdKey = Expression.Constant(Headers.CorrelationId);
        var correlationIdVar = Expression.Variable(typeof(string), "correlationIdStr");
        var tryGetValueMethod = typeof(IDictionary<string, string?>).GetMethod(
            nameof(IDictionary<,>.TryGetValue),
            [typeof(string), typeof(string).MakeByRefType()]
        )!;
        var isNullOrWhiteSpaceMethod = typeof(string).GetMethod(nameof(string.IsNullOrWhiteSpace), [typeof(string)])!;

        var correlationIdExpression = Expression.Block(
            [correlationIdVar],
            Expression.Condition(
                Expression.AndAlso(
                    Expression.Call(headersProperty, tryGetValueMethod, correlationIdKey, correlationIdVar),
                    Expression.Not(Expression.Call(isNullOrWhiteSpaceMethod, correlationIdVar))
                ),
                correlationIdVar,
                Expression.Constant(null, typeof(string))
            )
        );

        var correlationIdBinding = Expression.Bind(correlationIdProperty, correlationIdExpression);
        var tenantIdBinding = Expression.Bind(tenantIdProperty, tenantIdParam);
        var headersBinding = Expression.Bind(headersCtxProperty, consumeHeadersParam);
        var timestampBinding = Expression.Bind(
            timestampProperty,
            Expression.Call(
                typeof(ConsumeMiddlewarePipeline),
                nameof(_ResolveTimestamp),
                null,
                headersProperty,
                addedProperty
            )
        );
        var messageNameKey = Expression.Constant(Headers.MessageName);
        var topicBinding = Expression.Bind(
            topicProperty,
            Expression.Coalesce(
                Expression.MakeIndex(headersProperty, dictionaryIndexer, [messageNameKey]),
                Expression.Constant(string.Empty)
            )
        );

        var newExpr = Expression.MemberInit(
            Expression.New(consumeContextType),
            messageBinding,
            messageIdBinding,
            correlationIdBinding,
            tenantIdBinding,
            headersBinding,
            timestampBinding,
            topicBinding
        );

        var lambda = Expression.Lambda<Func<object, MediumMessage, MessageHeader, string?, object>>(
            newExpr,
            messageParam,
            mediumParam,
            consumeHeadersParam,
            tenantIdParam
        );
        return lambda.CompileFast();
    }

    private static ConsumeMiddlewareInvoker _CompileTypedInvoker(Type middlewareType, Type contextType)
    {
        var middlewareParam = Expression.Parameter(typeof(object), "middleware");
        var contextParam = Expression.Parameter(typeof(ConsumeContext), "context");
        var nextParam = Expression.Parameter(typeof(Func<ValueTask>), "next");
        var serviceType = typeof(IConsumeMiddleware<>).MakeGenericType(contextType);
        var invokeMethod = serviceType.GetMethod(nameof(IConsumeMiddleware<ConsumeContext>.InvokeAsync))!;

        var body = Expression.Call(
            Expression.Convert(middlewareParam, serviceType),
            invokeMethod,
            Expression.Convert(contextParam, contextType),
            nextParam
        );

        return Expression
            .Lambda<ConsumeMiddlewareInvoker>(body, middlewareParam, contextParam, nextParam)
            .CompileFast();
    }

    private static DateTimeOffset _ResolveTimestamp(IDictionary<string, string?> headers, DateTime added)
    {
        if (
            headers.TryGetValue(Headers.SentTime, out var sentTime)
            && !string.IsNullOrWhiteSpace(sentTime)
            && DateTimeOffset.TryParse(
                sentTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed
            )
        )
        {
            return parsed;
        }

        return new DateTimeOffset(DateTime.SpecifyKind(added, DateTimeKind.Utc), TimeSpan.Zero);
    }

    private static async Task _DispatchAsync(
        IMessageDispatcher dispatcher,
        IServiceProvider serviceProvider,
        ConsumerExecutorDescriptor descriptor,
        object consumeContext,
        Type messageType,
        CancellationToken cancellationToken
    )
    {
        var dispatchMethod = typeof(IMessageDispatcher)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(method =>
                method.Name == nameof(IMessageDispatcher.DispatchInScopeAsync)
                && method.GetParameters().Length == 4
                && method.GetParameters()[1].ParameterType == typeof(ConsumerExecutorDescriptor)
            )
            .MakeGenericMethod(messageType);

        Task task;
        try
        {
            task = (Task)
                dispatchMethod.Invoke(dispatcher, [serviceProvider, descriptor, consumeContext, cancellationToken])!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }

        await task.ConfigureAwait(false);
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

    private delegate ValueTask ConsumeMiddlewareInvoker(
        object middleware,
        ConsumeContext context,
        Func<ValueTask> next
    );
}
