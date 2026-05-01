// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using FastExpressionCompiler;
using Headless.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;

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
    IRuntimeConsumerRegistry runtimeRegistry
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
        var consumeHeaders = new MessageHeader(context.MediumMessage.Origin.Headers);
        var consumeContext = _BuildConsumeContext(messageInstance, context.MediumMessage, messageType, consumeHeaders);

        await using var scope = serviceProvider.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var filter = provider.GetService<IConsumeFilter>();
        object? resultObj = null;

        try
        {
            if (filter != null)
            {
                var executeParams = new object?[] { consumeContext, cancellationToken };
                var etContext = new ExecutingContext(context, executeParams);
                await filter.OnSubscribeExecutingAsync(etContext).ConfigureAwait(false);
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

            if (filter != null)
            {
                var edContext = new ExecutedContext(context, resultObj);
                await filter.OnSubscribeExecutedAsync(edContext).ConfigureAwait(false);
                resultObj = edContext.Result;
            }
        }
        catch (Exception e)
        {
            if (filter != null)
            {
                var exContext = new ExceptionContext(context, e);
                await filter.OnSubscribeExceptionAsync(exContext).ConfigureAwait(false);
                if (!exContext.ExceptionHandled)
                {
                    exContext.Exception.ReThrow();
                }

                if (exContext.Result != null)
                {
                    resultObj = exContext.Result;
                }
            }
            else
            {
                throw;
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
        MessageHeader headers
    )
    {
        var factory =
            (Func<object, MediumMessage, MessageHeader, object>)
                _compiledConsumeContextFactories.GetOrAdd(messageType, _CompileFactory);

        return factory(messageInstance, mediumMessage, headers);
    }

    private static Delegate _CompileFactory(Type messageType)
    {
        var consumeContextType = typeof(ConsumeContext<>).MakeGenericType(messageType);
        var messageParam = Expression.Parameter(typeof(object), "message");
        var mediumParam = Expression.Parameter(typeof(MediumMessage), "medium");
        var consumeHeadersParam = Expression.Parameter(typeof(MessageHeader), "headers");
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

        var tenantIdBinding = Expression.Bind(
            tenantIdProperty,
            Expression.Call(typeof(ConsumeExecutionPipeline), nameof(_ResolveTenantId), null, headersProperty)
        );

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

        var lambda = Expression.Lambda<Func<object, MediumMessage, MessageHeader, object>>(
            newExpr,
            messageParam,
            mediumParam,
            consumeHeadersParam
        );
        return lambda.CompileFast();
    }

    // Lenient consume-side tenant resolution: missing, whitespace, or oversized header values map
    // to null instead of failing the message. Mirrors the publish-side rules in
    // MessagePublishRequestFactory._ApplyTenantId / _ValidateTenantId (see #228).
    private static string? _ResolveTenantId(IDictionary<string, string?> headers)
    {
        if (!headers.TryGetValue(Headers.TenantId, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Length > PublishOptions.TenantIdMaxLength)
        {
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
