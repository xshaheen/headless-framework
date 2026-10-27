// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Messaging.Internal;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Processor;

internal sealed class MessageDelayedProcessor(
    ILogger<MessageDelayedProcessor> logger,
    IDispatcher dispatcher,
    IOptions<MessagingOptions> options
) : IProcessor
{
    private readonly TimeSpan _pollInterval = options.Value.DelayedMessagePollInterval;

    public async Task ProcessAsync(ProcessingContext context)
    {
        Argument.IsNotNull(context);

        var storage = context.Provider.GetRequiredService<IDataStorage>();
        // An outbox not initialized since startup is skipped until its initialization retry succeeds.
        var secondaries =
            context
                .Provider.GetService<MessagingOutboxes>()
                ?.Secondaries.Where(static outbox => outbox.IsInitialized)
                .ToArray()
            ?? [];

        if (secondaries.Length == 0)
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

        await context.WaitAsync(_pollInterval).ConfigureAwait(false);
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
                // A full scheduler would refuse every row this poll claims, and each refusal costs a hand-back write.
                if (committedDispatcher.IsSchedulerFull)
                {
                    return;
                }

                var messages = await claimStorage
                    .ClaimDelayedMessagesAsync(context.CancellationToken)
                    .ConfigureAwait(false);

                List<MediumMessage>? refused = null;
                foreach (var message in messages)
                {
                    message.OutboxStorage = outboxStorage;
                    if (!committedDispatcher.EnqueueCommittedDelayedMessage(message))
                    {
                        (refused ??= []).Add(message);
                    }
                }

                if (refused is not null)
                {
                    // The claim committed these rows as Queued and leased to this node, so left alone they wait out
                    // the lease. Handing them back under that exact lease makes them claimable on the next poll. The
                    // claim already committed, so a stopping token must not drop the hand-back.
                    await connection
                        .ChangePublishStateToDelayedAsync(refused, CancellationToken.None)
                        .ConfigureAwait(false);
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
        catch (OperationCanceledException) when (context.IsStopping)
        {
            // Shutdown, not a failed schedule; the poll-interval wait that follows surfaces the stop.
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
        EventId = 124,
        EventName = "GetDelayedMessagesFailed",
        Level = LogLevel.Warning,
        Message = "Get delayed messages from storage failed. Retrying..."
    )]
    internal static partial void LogGetDelayedMessagesFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 127,
        EventName = "ScheduleDelayedMessageFailed",
        Level = LogLevel.Error,
        Message = "Schedule delayed message failed!"
    )]
    internal static partial void LogScheduleDelayedMessageFailed(this ILogger logger, Exception exception);
}
