// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Logging;

namespace Framework.Messages.Processor;

public class InfiniteRetryProcessor(IProcessor inner, ILoggerFactory loggerFactory) : IProcessor
{
    private readonly ILogger _logger = loggerFactory.CreateLogger<InfiniteRetryProcessor>();

    public async Task ProcessAsync(ProcessingContext context)
    {
        while (!context.IsStopping)
        {
            try
            {
                await inner.ProcessAsync(context).AnyContext();
            }
            catch (OperationCanceledException)
            {
                //ignore
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Processor '{ProcessorName}' failed. Retrying...", inner.ToString());
                await context.WaitAsync(TimeSpan.FromSeconds(2)).AnyContext();
            }
        }
    }

    public override string? ToString()
    {
        return inner.ToString();
    }
}
