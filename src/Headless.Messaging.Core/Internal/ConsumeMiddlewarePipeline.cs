// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
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
    ILogger<ConsumeMiddlewarePipeline>? logger = null,
    IConsumeContextAccessor? consumeContextAccessor = null
) : IConsumeMiddlewarePipeline
{
    private static readonly ConcurrentDictionary<MiddlewareDispatchKey, ConsumeMiddlewareInvoker> _TypedInvokers =
        new();

    // Caches a compiled delegate per message type that calls the strongly-typed
    // IMessageDispatcher.DispatchInScopeAsync<TMessage>(...) overload, so the per-dispatch path avoids the
    // reflective GetMethods().Single(...).MakeGenericMethod(...).Invoke(...) the fallback used to run per message.
    private static readonly ConcurrentDictionary<Type, DispatchInvoker> _DispatchInvokers = new();

    // Caches the IConsumeMiddleware<TContext> closed service type per concrete ConsumeContext type, so the
    // resolution path avoids running MakeGenericType on every dispatch.
    private static readonly ConcurrentDictionary<Type, Type> _TypedMiddlewareServiceTypes = new();

    private readonly ConcurrentDictionary<
        Type,
        Func<object, MediumMessage, MessageHeader, string?, IntentType, object>
    > _compiledConsumeContextFactories = new();

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

        // Warn when the wire intent disagrees with the registered consumer intent so misconfigured
        // producers surface early without breaking the consume path.
        _ValidateIntentHeader(originHeaders, descriptor, logger);

        var consumeHeaders = new MessageHeader(originHeaders);
        var consumeContext = _BuildConsumeContext(
            messageInstance,
            context.MediumMessage,
            messageType,
            consumeHeaders,
            tenantId,
            descriptor.IntentType,
            cancellationToken
        );
        var previousConsumeContext = consumeContextAccessor?.Current;

        try
        {
            consumeContextAccessor?.Current = consumeContext;

            await using var scope = serviceProvider.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var middleware = _ResolveMiddleware(provider, consumeContext, descriptor.GroupName);
            var innerRingCompleted = false;

            Func<ValueTask> next = async () =>
            {
                if (
                    descriptor.HandlerId is { Length: > 0 } handlerId
                    && runtimeRegistry.TryGetInvoker(
                        descriptor.MessageName,
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
                consumeContext.MarkCompleted();
            };

            for (var i = middleware.Length - 1; i >= 0; i--)
            {
                var current = middleware[i];
                var innerNext = next;
                next = () => _InvokeAsync(current, consumeContext, innerNext, () => innerRingCompleted);
            }

            await next().ConfigureAwait(false);
        }
        finally
        {
            consumeContextAccessor?.Current = previousConsumeContext;
        }

        consumeHeaders.TryGetValue(Headers.CallbackName, out var callbackName);
        var callbackHeaders = consumeHeaders.ResponseHeader;
        return new ConsumerExecutedResult(
            consumeContext.Response,
            consumeContext.ResponseType,
            context.MediumMessage.Origin.GetId(),
            string.IsNullOrEmpty(callbackName) ? null : callbackName,
            callbackHeaders,
            consumeContext.ResponseCallbackName
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
            context.MarkCompleted();
        }
        catch (Exception ex) when (innerRingCompleted())
        {
            logger?.ConsumePostSuccessMiddlewareFailed(ex, middleware.GetType().FullName ?? middleware.GetType().Name);
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

    private static ValueTask _InvokeMiddlewareAsync(object middleware, ConsumeContext context, Func<ValueTask> next)
    {
        if (middleware is IConsumeMiddleware<ConsumeContext> busMiddleware)
        {
            return busMiddleware.InvokeAsync(context, next);
        }

        var invoker = _TypedInvokers.GetOrAdd(
            new MiddlewareDispatchKey(middleware.GetType(), context.MessageType),
            static (_, contextType) => _CompileTypedInvoker(contextType),
            context.GetType()
        );

        return invoker(middleware, context, next);
    }

    private object[] _ResolveMiddleware(IServiceProvider provider, ConsumeContext context, string? groupName)
    {
        // _ResolveDirectMiddleware already materializes a fresh array; reuse it directly instead of copying again.
        var directMiddleware = _ResolveDirectMiddleware(provider, context);

        if (
            descriptorRegistry is not null
            && descriptorRegistry.TryGetConsumeDescriptors(context.MessageType, groupName, out var descriptors)
        )
        {
            return
            [
                .. descriptors
                    .Select(descriptor => _ResolveDescriptor(provider, descriptor))
                    .Where(static middleware => middleware is not null)
                    .Cast<object>(),
                .. _GetUntrackedDirectMiddleware(directMiddleware, MiddlewareDirection.Consume),
            ];
        }

        return directMiddleware;
    }

    private static object[] _ResolveDirectMiddleware(IServiceProvider provider, ConsumeContext context)
    {
        var typedServiceType = _TypedMiddlewareServiceTypes.GetOrAdd(
            context.GetType(),
            static contextType => typeof(IConsumeMiddleware<>).MakeGenericType(contextType)
        );
        var busMiddleware = provider.GetServices<IConsumeMiddleware<ConsumeContext>>().Cast<object>();
        var typedMiddleware = provider
            .GetServices(typedServiceType)
            .Where(static middleware => middleware is not null)
            .Cast<object>();

        return [.. busMiddleware, .. typedMiddleware];
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

    private ConsumeContext _BuildConsumeContext(
        object messageInstance,
        MediumMessage mediumMessage,
        Type messageType,
        MessageHeader headers,
        string? tenantId,
        IntentType intentType,
        CancellationToken cancellationToken
    )
    {
        var factory = _compiledConsumeContextFactories.GetOrAdd(messageType, _CompileFactory);
        var context = (ConsumeContext)factory(messageInstance, mediumMessage, headers, tenantId, intentType);
        context.SetCancellationToken(cancellationToken);

        return context;
    }

    private static Func<object, MediumMessage, MessageHeader, string?, IntentType, object> _CompileFactory(
        Type messageType
    )
    {
        var consumeContextType = typeof(ConsumeContext<>).MakeGenericType(messageType);
        var messageParam = Expression.Parameter(typeof(object), "message");
        var mediumParam = Expression.Parameter(typeof(MediumMessage), "medium");
        var consumeHeadersParam = Expression.Parameter(typeof(MessageHeader), "headers");
        var tenantIdParam = Expression.Parameter(typeof(string), "tenantId");
        var intentTypeParam = Expression.Parameter(typeof(IntentType), "intentType");
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
        var topicProperty = consumeContextType.GetProperty(nameof(ConsumeContext<>.MessageName))!;
        var intentTypeProperty = consumeContextType.GetProperty(nameof(ConsumeContext<>.IntentType))!;

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
        var intentTypeBinding = Expression.Bind(intentTypeProperty, intentTypeParam);
        var headersBinding = Expression.Bind(headersCtxProperty, consumeHeadersParam);
        var timestampBinding = Expression.Bind(
            timestampProperty,
            Expression.Call(
                typeof(ConsumeMiddlewarePipeline),
                nameof(_ResolveTimestamp),
                typeArguments: null,
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
            intentTypeBinding,
            headersBinding,
            timestampBinding,
            topicBinding
        );

        var lambda = Expression.Lambda<Func<object, MediumMessage, MessageHeader, string?, IntentType, object>>(
            newExpr,
            messageParam,
            mediumParam,
            consumeHeadersParam,
            tenantIdParam,
            intentTypeParam
        );
        return lambda.CompileFast();
    }

    private static ConsumeMiddlewareInvoker _CompileTypedInvoker(Type contextType)
    {
        var middlewareParam = Expression.Parameter(typeof(object), "middleware");
        var contextParam = Expression.Parameter(typeof(ConsumeContext), "context");
        var nextParam = Expression.Parameter(typeof(Func<ValueTask>), "next");
        var serviceType = typeof(IConsumeMiddleware<>).MakeGenericType(contextType);
        var invokeMethod = serviceType.GetMethod(nameof(IConsumeMiddleware<>.InvokeAsync))!;

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

    private static Task _DispatchAsync(
        IMessageDispatcher dispatcher,
        IServiceProvider serviceProvider,
        ConsumerExecutorDescriptor descriptor,
        object consumeContext,
        Type messageType,
        CancellationToken cancellationToken
    )
    {
        var invoker = _DispatchInvokers.GetOrAdd(messageType, _CompileDispatchInvoker);

        // Calling the compiled delegate invokes the generic overload directly, so handler exceptions propagate
        // unwrapped (no TargetInvocationException) — the same observable result the old reflective unwrap produced.
        return invoker(dispatcher, serviceProvider, descriptor, consumeContext, cancellationToken);
    }

    private static DispatchInvoker _CompileDispatchInvoker(Type messageType)
    {
        var consumeContextType = typeof(ConsumeContext<>).MakeGenericType(messageType);

        var dispatchMethod = typeof(IMessageDispatcher)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Single(method =>
                string.Equals(method.Name, nameof(IMessageDispatcher.DispatchInScopeAsync), StringComparison.Ordinal)
                && method.GetParameters().Length == 4
                && method.GetParameters()[1].ParameterType == typeof(ConsumerExecutorDescriptor)
            )
            .MakeGenericMethod(messageType);

        var dispatcherParam = Expression.Parameter(typeof(IMessageDispatcher), "dispatcher");
        var serviceProviderParam = Expression.Parameter(typeof(IServiceProvider), "serviceProvider");
        var descriptorParam = Expression.Parameter(typeof(ConsumerExecutorDescriptor), "descriptor");
        var consumeContextParam = Expression.Parameter(typeof(object), "consumeContext");
        var cancellationTokenParam = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

        var body = Expression.Call(
            dispatcherParam,
            dispatchMethod,
            serviceProviderParam,
            descriptorParam,
            Expression.Convert(consumeContextParam, consumeContextType),
            cancellationTokenParam
        );

        return Expression
            .Lambda<DispatchInvoker>(
                body,
                dispatcherParam,
                serviceProviderParam,
                descriptorParam,
                consumeContextParam,
                cancellationTokenParam
            )
            .CompileFast();
    }

    private static void _ValidateIntentHeader(
        IDictionary<string, string?> headers,
        ConsumerExecutorDescriptor descriptor,
        ILogger? logger
    )
    {
        if (
            logger is null
            || !headers.TryGetValue(Headers.Intent, out var wireIntent)
            || string.IsNullOrWhiteSpace(wireIntent)
        )
        {
            return;
        }

        var consumerIntent = descriptor.IntentType.ToString();
        if (!string.Equals(wireIntent, consumerIntent, StringComparison.OrdinalIgnoreCase))
        {
            logger.ConsumeIntentMismatch(descriptor.MessageName, wireIntent, consumerIntent);
        }
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

    private delegate Task DispatchInvoker(
        IMessageDispatcher dispatcher,
        IServiceProvider serviceProvider,
        ConsumerExecutorDescriptor descriptor,
        object consumeContext,
        CancellationToken cancellationToken
    );
}
