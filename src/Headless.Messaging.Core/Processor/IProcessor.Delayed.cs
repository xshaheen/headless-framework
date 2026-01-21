// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data.Common;
using Framework.Checks;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Processor;

public class MessageDelayedProcessor(ILogger<MessageDelayedProcessor> logger, IDispatcher dispatcher) : IProcessor
{
    private readonly TimeSpan _waitingInterval = TimeSpan.FromSeconds(60);

    public virtual async Task ProcessAsync(ProcessingContext context)
    {
        Argument.IsNotNull(context);

        var storage = context.Provider.GetRequiredService<IDataStorage>();

        await _ProcessDelayedAsync(storage, context).AnyContext();

        await context.WaitAsync(_waitingInterval).AnyContext();
    }

    private async Task _ProcessDelayedAsync(IDataStorage connection, ProcessingContext context)
    {
        try
        {
            async Task scheduleTask(object transaction, IEnumerable<MediumMessage> messages)
            {
                foreach (var message in messages)
                {
                    await dispatcher
                        .EnqueueToScheduler(message, message.ExpiresAt!.Value, transaction, context.CancellationToken)
                        .AnyContext();
                }
            }

            await connection.ScheduleMessagesOfDelayedAsync(scheduleTask, context.CancellationToken).AnyContext();
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
