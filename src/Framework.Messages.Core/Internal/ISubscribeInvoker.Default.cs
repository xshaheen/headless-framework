// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Messages;
using Framework.Messages.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Messages.Internal;

public class SubscribeInvoker(IServiceProvider serviceProvider, ISerializer serializer) : ISubscribeInvoker
{
    public async Task<ConsumerExecutedResult> InvokeAsync(
        ConsumerContext context,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mediumMessage = context.MediumMessage;
        var descriptor = context.ConsumerDescriptor;

        // Extract message type from method parameter: IConsume<T>.Consume(ConsumeContext<T>, CancellationToken)
        var consumeContextParam = descriptor.Parameters.FirstOrDefault(p =>
            p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(ConsumeContext<>)
        );

        if (consumeContextParam == null)
        {
            throw new InvalidOperationException(
                $"Consumer method must have a ConsumeContext<T> parameter. Method: {descriptor.MethodInfo.Name}"
            );
        }

        var messageType = consumeContextParam.ParameterType.GetGenericArguments()[0];

        await using var scope = serviceProvider.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        // Deserialize message
        object? messageInstance = null;
        if (mediumMessage.Origin.Value != null)
        {
            if (serializer.IsJsonType(mediumMessage.Origin.Value))
            {
                // Value is already a JsonElement
                messageInstance = serializer.Deserialize(mediumMessage.Origin.Value, messageType);
            }
            else if (mediumMessage.Origin.Value is string jsonString)
            {
                // Value is a JSON string - deserialize it
                messageInstance = System.Text.Json.JsonSerializer.Deserialize(jsonString, messageType);
            }
            else if (messageType.IsInstanceOfType(mediumMessage.Origin.Value))
            {
                // Value is already the correct type
                messageInstance = mediumMessage.Origin.Value;
            }
            else
            {
                messageInstance = Convert.ChangeType(mediumMessage.Origin.Value, messageType);
            }
        }

        if (messageInstance == null)
        {
            throw new InvalidOperationException($"Failed to deserialize message of type {messageType.Name}");
        }

        // Build ConsumeContext<T>
        var consumeContext = _BuildConsumeContext(messageInstance, mediumMessage, messageType);

        // Get IMessageDispatcher
        var dispatcher = provider.GetRequiredService<IMessageDispatcher>();

        // Execute filters and invoke handler
        var filter = provider.GetService<IConsumeFilter>();
        object? resultObj = null;

        try
        {
            if (filter != null)
            {
                var executeParams = new object?[] { consumeContext, cancellationToken };
                var etContext = new ExecutingContext(context, executeParams);
                await filter.OnSubscribeExecutingAsync(etContext).AnyContext();
            }

            // Dispatch via compiled dispatcher (5-8x faster than reflection)
            await _DispatchAsync(dispatcher, consumeContext, messageType, cancellationToken);

            if (filter != null)
            {
                var edContext = new ExecutedContext(context, resultObj);
                await filter.OnSubscribeExecutedAsync(edContext).AnyContext();
                resultObj = edContext.Result;
            }
        }
        catch (Exception e)
        {
            if (filter != null)
            {
                var exContext = new ExceptionContext(context, e);
                await filter.OnSubscribeExceptionAsync(exContext).AnyContext();
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

        var callbackName = mediumMessage.Origin.GetCallbackName();
        if (string.IsNullOrEmpty(callbackName))
        {
            return new ConsumerExecutedResult(resultObj, mediumMessage.Origin.GetId(), null, null);
        }
        else
        {
            return new ConsumerExecutedResult(resultObj, mediumMessage.Origin.GetId(), callbackName, null);
        }
    }

    private static object _BuildConsumeContext(object messageInstance, MediumMessage mediumMessage, Type messageType)
    {
        var consumeContextType = typeof(ConsumeContext<>).MakeGenericType(messageType);

        // Parse IDs
        var messageId = Guid.Parse(mediumMessage.Origin.GetId());

        // Read correlation ID from headers (nullable)
        Guid? correlationId = null;
        if (
            mediumMessage.Origin.Headers.TryGetValue(Headers.CorrelationId, out var correlationIdStr)
            && !string.IsNullOrEmpty(correlationIdStr)
        )
        {
            correlationId = Guid.Parse(correlationIdStr);
        }

        // Parse timestamp
        var timestamp = new DateTimeOffset(mediumMessage.Added, TimeSpan.Zero);

        // Create ConsumeContext<T>
        var instance = Activator.CreateInstance(consumeContextType);
        if (instance == null)
        {
            throw new InvalidOperationException($"Failed to create ConsumeContext<{messageType.Name}>");
        }

        // Set required properties using reflection (ConsumeContext uses required init properties)
        var messageProperty = consumeContextType.GetProperty(nameof(ConsumeContext<object>.Message))!;
        var messageIdProperty = consumeContextType.GetProperty(nameof(ConsumeContext<object>.MessageId))!;
        var correlationIdProperty = consumeContextType.GetProperty(nameof(ConsumeContext<object>.CorrelationId))!;
        var headersProperty = consumeContextType.GetProperty(nameof(ConsumeContext<object>.Headers))!;
        var timestampProperty = consumeContextType.GetProperty(nameof(ConsumeContext<object>.Timestamp))!;
        var topicProperty = consumeContextType.GetProperty(nameof(ConsumeContext<object>.Topic))!;

        messageProperty.SetValue(instance, messageInstance);
        messageIdProperty.SetValue(instance, messageId);
        correlationIdProperty.SetValue(instance, correlationId);
        headersProperty.SetValue(instance, new MessageHeader(mediumMessage.Origin.Headers));
        timestampProperty.SetValue(instance, timestamp);
        topicProperty.SetValue(instance, mediumMessage.Origin.GetName());

        return instance;
    }

    private static async Task _DispatchAsync(
        IMessageDispatcher dispatcher,
        object consumeContext,
        Type messageType,
        CancellationToken cancellationToken
    )
    {
        // Call IMessageDispatcher.DispatchAsync<T>(ConsumeContext<T>, CancellationToken)
        var dispatchMethod = typeof(IMessageDispatcher)
            .GetMethod(nameof(IMessageDispatcher.DispatchAsync))!
            .MakeGenericMethod(messageType);

        var task = (Task)dispatchMethod.Invoke(dispatcher, [consumeContext, cancellationToken])!;
        await task.AnyContext();
    }
}
