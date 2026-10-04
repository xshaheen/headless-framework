// Copyright (c) Mahmoud Shaheen. All rights reserved.

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

    Task<ConsumerExecutedResult> InvokeInScopeAsync(
        ConsumerContext context,
        IServiceProvider services,
        CancellationToken cancellationToken = default
    );
}

internal sealed class SubscribeInvoker(IMessageSerializer serializer, IConsumeMiddlewarePipeline executionPipeline)
    : ISubscribeInvoker
{
    public Task<ConsumerExecutedResult> InvokeAsync(
        ConsumerContext context,
        CancellationToken cancellationToken = default
    ) => _InvokeAsync(context, services: null, cancellationToken);

    public Task<ConsumerExecutedResult> InvokeInScopeAsync(
        ConsumerContext context,
        IServiceProvider services,
        CancellationToken cancellationToken = default
    ) => _InvokeAsync(context, services, cancellationToken);

    private async Task<ConsumerExecutedResult> _InvokeAsync(
        ConsumerContext context,
        IServiceProvider? services,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var mediumMessage = context.MediumMessage;
        var descriptor = context.ConsumerDescriptor;

        var messageType =
            descriptor.MessageType
            ?? throw new InvalidOperationException(
                $"Consumer {descriptor.ConsumerType.Name} of message '{descriptor.MessageName}' declares no message type."
            );

        // Deserialize message
        object? messageInstance = null;
        var originValue = mediumMessage.Origin.Value;

        if (originValue != null)
        {
            try
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
                    throw new MessageDeserializationException(
                        $"Unsupported message value type: {originValue.GetType().Name}. "
                            + $"Expected JSON string, JsonElement, or {messageType.Name}"
                    );
                }
            }
            catch (Exception ex) when (ex is not MessageDeserializationException)
            {
                throw new MessageDeserializationException(
                    $"Failed to deserialize message of type {messageType.Name}.",
                    ex
                );
            }
        }

        if (messageInstance == null)
        {
            throw new MessageDeserializationException(MessageDeserializationException.EmptyBody(messageType));
        }

        return services is null
            ? await executionPipeline
                .ExecuteAsync(context, messageInstance, messageType, cancellationToken)
                .ConfigureAwait(false)
            : await executionPipeline
                .ExecuteInScopeAsync(context, messageInstance, messageType, services, cancellationToken)
                .ConfigureAwait(false);
    }
}
