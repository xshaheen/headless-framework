// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Processor;

internal sealed class MessageDelayedProcessor(ILogger<MessageDelayedProcessor> logger, IDispatcher dispatcher)
    : IProcessor
{
    private readonly TimeSpan _waitingInterval = TimeSpan.FromSeconds(60);

    public async Task ProcessAsync(ProcessingContext context)
    {
        Argument.IsNotNull(context);

        var storage = context.Provider.GetRequiredService<IDataStorage>();
        var secondaries = context.Provider.GetService<MessagingOutboxes>()?.Secondaries ?? [];

        if (secondaries.Count == 0)
        {
            await _ProcessDelayedAsync(storage, outboxStorage: null, context).ConfigureAwait(false);
        }
        else
        {
            // Concurrently, and each outbox absorbs its own failure, so a database that hangs until its command
            // timeout never delays another database's due messages.
            await Task.WhenAll(
                    secondaries
                        .Select(outbox => _ProcessDelayedAsync(outbox.Storage, outbox.Storage, context))
                        .Prepend(_ProcessDelayedAsync(storage, outboxStorage: null, context))
                )
                .ConfigureAwait(false);
        }

        await context.WaitAsync(_waitingInterval).ConfigureAwait(false);
    }

    private async Task _ProcessDelayedAsync(
        IDataStorage connection,
        IDataStorage? outboxStorage,
        ProcessingContext context
    )
    {
        try
        {
            if (
                connection is IDelayedMessageClaimStorage claimStorage
                && dispatcher is ICommittedDelayedMessageDispatcher committedDispatcher
            )
            {
                var messages = await claimStorage
                    .ClaimDelayedMessagesAsync(context.CancellationToken)
                    .ConfigureAwait(false);

                foreach (var message in messages)
                {
                    message.OutboxStorage = outboxStorage;
                    committedDispatcher.EnqueueCommittedDelayedMessage(message);
                }

                return;
            }

            async ValueTask scheduleTask(DbTransaction? transaction, IEnumerable<MediumMessage> messages)
            {
                foreach (var message in messages)
                {
                    message.OutboxStorage = outboxStorage;
                    await dispatcher
                        .EnqueueToScheduler(message, message.ExpiresAt!.Value, transaction, context.CancellationToken)
                        .ConfigureAwait(false);
                }
            }

            await connection
                .ScheduleMessagesOfDelayedAsync(scheduleTask, context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (DbException e)
        {
            logger.LogGetDelayedMessagesFailed(e);
        }
        catch (Exception ex)
        {
            logger.LogScheduleDelayedMessageFailed(ex);
        }
    }
}

internal static partial class MessageDelayedProcessorLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "GetDelayedMessagesFailed",
        Level = LogLevel.Warning,
        Message = "Get delayed messages from storage failed. Retrying..."
    )]
    public static partial void LogGetDelayedMessagesFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2,
        EventName = "ScheduleDelayedMessageFailed",
        Level = LogLevel.Error,
        Message = "Schedule delayed message failed!"
    )]
    public static partial void LogScheduleDelayedMessageFailed(this ILogger logger, Exception exception);
}
