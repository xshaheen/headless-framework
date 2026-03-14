// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using FastExpressionCompiler;
using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Internal;

/// <summary>
/// Perform user definition method of consumers.
/// </summary>
public interface ISubscribeInvoker
{
    /// <summary>
    /// Invoke subscribe method with the consumer context.
    /// </summary>
    /// <param name="context">consumer execute context</param>
    /// <param name="cancellationToken">The object of <see cref="CancellationToken" />.</param>
    Task<ConsumerExecutedResult> InvokeAsync(ConsumerContext context, CancellationToken cancellationToken = default);
}

internal sealed class SubscribeInvoker(ISerializer serializer, IConsumeExecutionPipeline executionPipeline)
    : ISubscribeInvoker
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
                throw new InvalidOperationException(
                    $"Unsupported message value type: {mediumMessage.Origin.Value?.GetType().Name}. "
                        + $"Expected JSON string, JsonElement, or {messageType.Name}"
                );
            }
        }

        if (messageInstance == null)
        {
            throw new InvalidOperationException($"Failed to deserialize message of type {messageType.Name}");
        }

        return await executionPipeline.ExecuteAsync(context, messageInstance, messageType, cancellationToken)
            .ConfigureAwait(false);
    }
}
