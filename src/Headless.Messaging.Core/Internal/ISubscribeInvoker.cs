// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;
using Headless.Messaging.Serialization;

namespace Headless.Messaging.Internal;

/// <summary>
/// Perform user definition method of consumers.
/// </summary>
internal interface ISubscribeInvoker
{
    /// <summary>
    /// Invoke subscribe method with the consumer context.
    /// </summary>
    /// <param name="context">consumer execute context</param>
    /// <param name="cancellationToken">The object of <see cref="CancellationToken" />.</param>
    Task<ConsumerExecutedResult> InvokeAsync(ConsumerContext context, CancellationToken cancellationToken = default);
}

internal sealed class SubscribeInvoker(ISerializer serializer, IConsumeMiddlewarePipeline executionPipeline)
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

        // Extract message type from method parameter: IConsume<T>.Consume(ConsumeContext<T>, CancellationToken).
        // Cached on the descriptor - recomputing it per execute is pure reflection overhead.
        var messageType =
            descriptor.ConsumeContextValueType
            ?? throw new InvalidOperationException(
                $"Consumer method must have a ConsumeContext<T> parameter. Method: {descriptor.MethodInfo.Name}"
            );

        // Deserialize message
        object? messageInstance = null;
        var originValue = mediumMessage.Origin.Value;

        if (originValue != null)
        {
            if (serializer.IsJsonType(originValue))
            {
                // Value is already a JsonElement
                messageInstance = serializer.Deserialize(originValue, messageType);
            }
            else if (originValue is string jsonString)
            {
                // Value is a JSON string - deserialize it
                messageInstance = JsonSerializer.Deserialize(jsonString, messageType);
            }
            else if (messageType.IsInstanceOfType(originValue))
            {
                // Value is already the correct type
                messageInstance = originValue;
            }
            else
            {
                throw new InvalidOperationException(
                    $"Unsupported message value type: {originValue.GetType().Name}. "
                        + $"Expected JSON string, JsonElement, or {messageType.Name}"
                );
            }
        }

        if (messageInstance == null)
        {
            throw new InvalidOperationException($"Failed to deserialize message of type {messageType.Name}");
        }

        return await executionPipeline
            .ExecuteAsync(context, messageInstance, messageType, cancellationToken)
            .ConfigureAwait(false);
    }
}
