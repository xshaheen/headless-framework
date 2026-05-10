// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using FastExpressionCompiler;
using Headless.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Internal;

internal interface IConsumeExecutionPipeline
{
    Task<ConsumerExecutedResult> ExecuteAsync(
        ConsumerContext context,
        object messageInstance,
        Type messageType,
        CancellationToken cancellationToken = default
    );
}

internal sealed class ConsumeExecutionPipeline(
    IServiceProvider serviceProvider,
    IRuntimeConsumerRegistry runtimeRegistry,
    ILogger<ConsumeExecutionPipeline>? logger = null
) : IConsumeExecutionPipeline
{
    private readonly ConcurrentDictionary<Type, Delegate> _compiledConsumeContextFactories = new();

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
        var tenantId = _ResolveTenantId(originHeaders, logger);
        var consumeHeaders = new MessageHeader(originHeaders);
        var consumeContext = _BuildConsumeContext(
            messageInstance,
            context.MediumMessage,
            messageType,
            consumeHeaders,
            tenantId
        );

        await using var scope = serviceProvider.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        // Filter chain: executing in registration order, executed/exception in reverse — mirrors ASP.NET MVC.
        var filters = provider.GetServices<IConsumeFilter>().ToArray();
        // Tracks how many executing-phase filters completed; only those participate in the exception phase
        // when an early filter throws during executing — matches ASP.NET MVC stack-discipline semantics.
        var enteredCount = 0;
        object? resultObj = null;

        try
        {
            var executeParams = new[] { consumeContext, cancellationToken };
            var etContext = new ExecutingContext(context, executeParams, tenantId);
            for (var i = 0; i < filters.Length; i++)
            {
                // Increment before the call so a filter that throws during executing is counted
                // as "entered" and gets its exception phase invoked during stack unwind.
                enteredCount = i + 1;
                await filters[i].OnSubscribeExecutingAsync(etContext, cancellationToken).ConfigureAwait(false);
            }

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
                await runtimeInvoker.InvokeAsync(consumeContext, provider, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var dispatcher = provider.GetRequiredService<IMessageDispatcher>();
                await _DispatchAsync(dispatcher, provider, descriptor, consumeContext, messageType, cancellationToken)
                    .ConfigureAwait(false);
            }

            var edContext = new ExecutedContext(context, resultObj);
            for (var i = filters.Length - 1; i >= 0; i--)
            {
                try
                {
                    await filters[i].OnSubscribeExecutedAsync(edContext, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception filterEx)
                {
                    // Consumer body already committed. Propagating an after-success filter failure —
                    // including OperationCanceledException — would surface to the transport as a
                    // consume-failure and trigger a spurious retry of an already-handled message.
                    // Cancellation has no operational meaning once the consumer body has committed.
                    logger?.SubscribeExecutedFilterFailed(
                        filterEx,
                        filters[i].GetType().FullName ?? filters[i].GetType().Name
                    );
                }
            }
            resultObj = edContext.Result;
        }
        catch (Exception e)
        {
            if (enteredCount == 0)
            {
                throw;
            }

            var exContext = new ExceptionContext(context, e);
            // Only filters whose executing phase completed participate; reverse stack-unwind order.
            for (var i = enteredCount - 1; i >= 0; i--)
            {
                try
                {
                    await filters[i].OnSubscribeExceptionAsync(exContext, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception nested)
                {
                    // Preserve the original consumer exception identity for the eventual ReThrow().
                    // A throwing exception-phase filter must not silently replace the original failure
                    // or skip the rest of the chain.
                    logger?.SubscribeExceptionFilterFailed(
                        nested,
                        filters[i].GetType().FullName ?? filters[i].GetType().Name
                    );
                }
            }

            // Cancellation is never swallowable: ignore ExceptionHandled when the original failure
            // was an OperationCanceledException so the host always observes the cancel.
            // Mirrors IPublishExecutionPipeline:123.
            if (!exContext.ExceptionHandled || exContext.Exception is OperationCanceledException)
            {
                exContext.Exception.ReThrow();
            }

            if (exContext.Result != null)
            {
                resultObj = exContext.Result;
            }
        }

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

    private object _BuildConsumeContext(
        object messageInstance,
        MediumMessage mediumMessage,
        Type messageType,
        MessageHeader headers,
        string? tenantId
    )
    {
        var factory =
            (Func<object, MediumMessage, MessageHeader, string?, object>)
                _compiledConsumeContextFactories.GetOrAdd(messageType, _CompileFactory);

        return factory(messageInstance, mediumMessage, headers, tenantId);
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

        var messageProperty = consumeContextType.GetProperty(nameof(ConsumeContext<>.Message))!;
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
                typeof(ConsumeExecutionPipeline),
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

    // Lenient consume-side tenant resolution: missing, whitespace, or oversized header values map
    // to null instead of failing the message. Mirrors the publish-side rules in
    // MessagePublishRequestFactory._ApplyTenantId / _ValidateTenantId (see #228).
    // Oversized values emit a structured warning so operators can detect a misbehaving producer
    // (or a producer-side downgrade attack) without changing the lenient behavior contract.
    private static string? _ResolveTenantId(IDictionary<string, string?> headers, ILogger? logger)
    {
        if (!headers.TryGetValue(Headers.TenantId, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length > PublishOptions.TenantIdMaxLength)
        {
            logger?.TenantIdHeaderRejected(value.Length);
            return null;
        }

        return value;
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

        var task = (Task)
            dispatchMethod.Invoke(dispatcher, [serviceProvider, descriptor, consumeContext, cancellationToken])!;
        await task.ConfigureAwait(false);
    }
}
