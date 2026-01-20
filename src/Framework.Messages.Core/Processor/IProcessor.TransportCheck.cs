// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Framework.Messages.Internal;
using Microsoft.Extensions.Logging;

namespace Framework.Messages.Processor;

public class TransportCheckProcessor(ILogger<TransportCheckProcessor> logger, IConsumerRegister register) : IProcessor
{
    private readonly TimeSpan _waitingInterval = TimeSpan.FromSeconds(30);

    public virtual async Task ProcessAsync(ProcessingContext context)
    {
        Argument.IsNotNull(context);

        context.ThrowIfStopping();

        logger.LogDebug("Transport connection checking...");

        if (!register.IsHealthy())
        {
            logger.LogWarning("Transport connection is unhealthy, reconnection...");

            await register.ReStartAsync();
        }
        else
        {
            logger.LogDebug("Transport connection healthy!");
        }

        await context.WaitAsync(_waitingInterval).AnyContext();
    }
}
