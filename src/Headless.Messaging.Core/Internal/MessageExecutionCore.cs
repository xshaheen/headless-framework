// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Internal;

internal interface IMessageExecutionCore
{
    ValueTask ExecuteAsync<TMessage>(
        ConsumeContext<TMessage> context,
        IServiceProvider scopedServiceProvider,
        Func<IServiceProvider, ConsumeContext<TMessage>, CancellationToken, ValueTask> invoke,
        CancellationToken cancellationToken
    )
        where TMessage : class;

    ValueTask ExecuteAsync(
        string messageId,
        string? correlationId,
        Func<CancellationToken, ValueTask> invoke,
        CancellationToken cancellationToken
    );
}

internal sealed class MessageExecutionCore : IMessageExecutionCore
{
    public async ValueTask ExecuteAsync<TMessage>(
        ConsumeContext<TMessage> context,
        IServiceProvider scopedServiceProvider,
        Func<IServiceProvider, ConsumeContext<TMessage>, CancellationToken, ValueTask> invoke,
        CancellationToken cancellationToken
    )
        where TMessage : class
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(scopedServiceProvider);
        Argument.IsNotNull(invoke);

        await ExecuteAsync(
                context.MessageId,
                context.CorrelationId,
                ct => invoke(scopedServiceProvider, context, ct),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask ExecuteAsync(
        string messageId,
        string? correlationId,
        Func<CancellationToken, ValueTask> invoke,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNullOrWhiteSpace(messageId);
        Argument.IsNotNull(invoke);

        cancellationToken.ThrowIfCancellationRequested();

        var resolvedCorrelationId = correlationId ?? messageId;
        using var correlationScope = MessagingCorrelationScope.Begin(resolvedCorrelationId);

        await invoke(cancellationToken).ConfigureAwait(false);
    }
}
