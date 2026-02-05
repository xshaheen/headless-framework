// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Headless.Checks;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Processor;

public sealed class MessageDelayedProcessor(ILogger<MessageDelayedProcessor> logger, IDispatcher dispatcher)
    : IProcessor
{
    private readonly TimeSpan _waitingInterval = TimeSpan.FromSeconds(60);

    public async Task ProcessAsync(ProcessingContext context)
    {
        Argument.IsNotNull(context);

        var storage = context.Provider.GetRequiredService<IDataStorage>();

        await _ProcessDelayedAsync(storage, context).ConfigureAwait(false);

        await context.WaitAsync(_waitingInterval).ConfigureAwait(false);
    }

    private async Task _ProcessDelayedAsync(IDataStorage connection, ProcessingContext context)
    {
        try
        {
            async ValueTask scheduleTask(object transaction, IEnumerable<MediumMessage> messages)
            {
                foreach (var message in messages)
                {
                    await dispatcher
                        .EnqueueToScheduler(message, message.ExpiresAt!.Value, transaction, context.CancellationToken)
                        .ConfigureAwait(false);
                }
            }

            await connection
                .ScheduleMessagesOfDelayedAsync(scheduleTask, context.CancellationToken)
                .ConfigureAwait(false);
        }
        catch (DbException ex)
        {
            logger.LogWarning(ex, "Get delayed messages from storage failed. Retrying...");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Schedule delayed message failed!");
        }
    }
}
