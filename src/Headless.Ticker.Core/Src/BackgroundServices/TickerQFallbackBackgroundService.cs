using Headless.Ticker.Interfaces.Managers;
using Headless.Ticker.TickerQThreadPool;
using Microsoft.Extensions.Hosting;

namespace Headless.Ticker.BackgroundServices;

internal class TickerQFallbackBackgroundService(
    IInternalTickerManager internalTickerManager,
    SchedulerOptionsBuilder schedulerOptions,
    TickerExecutionTaskHandler tickerExecutionTaskHandler,
    TickerQTaskScheduler tickerQTaskScheduler
) : BackgroundService
{
    private int _started;
    private readonly TickerExecutionTaskHandler _tickerExecutionTaskHandler = tickerExecutionTaskHandler;
    private readonly TimeSpan _fallbackJobPeriod = schedulerOptions.FallbackIntervalChecker;

    public override Task StartAsync(CancellationToken ct)
    {
        return Interlocked.CompareExchange(ref _started, 1, 0) != 0 ? Task.CompletedTask : base.StartAsync(ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // If the scheduler is frozen or disposed (e.g., manual start mode or shutdown),
                // skip queuing fallback work to avoid throwing and stopping the host.
                if (tickerQTaskScheduler.IsFrozen || tickerQTaskScheduler.IsDisposed)
                {
                    await Task.Delay(_fallbackJobPeriod, stoppingToken);
                    continue;
                }

                var functions = await internalTickerManager.RunTimedOutTickers(stoppingToken);

                if (functions.Length != 0)
                {
                    foreach (var function in functions)
                    {
                        if (
                            TickerFunctionProvider.TickerFunctions.TryGetValue(
                                function.FunctionName,
                                out var tickerItem
                            )
                        )
                        {
                            function.CachedDelegate = tickerItem.Delegate;
                            function.CachedPriority = tickerItem.Priority;
                        }

                        foreach (var child in function.TimeTickerChildren)
                        {
                            if (
                                TickerFunctionProvider.TickerFunctions.TryGetValue(
                                    child.FunctionName,
                                    out var childItem
                                )
                            )
                            {
                                child.CachedDelegate = childItem.Delegate;
                                child.CachedPriority = childItem.Priority;
                            }

                            foreach (var grandChild in child.TimeTickerChildren)
                            {
                                if (
                                    TickerFunctionProvider.TickerFunctions.TryGetValue(
                                        grandChild.FunctionName,
                                        out var grandChildItem
                                    )
                                )
                                {
                                    grandChild.CachedDelegate = grandChildItem.Delegate;
                                    grandChild.CachedPriority = grandChildItem.Priority;
                                }
                            }
                        }

                        try
                        {
                            await tickerQTaskScheduler.QueueAsync(
                                ct => _tickerExecutionTaskHandler.ExecuteTaskAsync(function, true, ct),
                                function.CachedPriority,
                                stoppingToken
                            );
                        }
                        catch (InvalidOperationException)
                            when (tickerQTaskScheduler.IsFrozen || tickerQTaskScheduler.IsDisposed)
                        {
                            // Scheduler is frozen/disposed – ignore and let loop delay
                            break;
                        }
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(10), stoppingToken);
                }
                else
                {
                    await Task.Delay(_fallbackJobPeriod, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Host is shutting down – exit gracefully.
                break;
            }
            // ERP022: Background service must continue running even if individual operations fail.
#pragma warning disable ERP022
            catch (Exception)
            {
                // Swallow unexpected exceptions so they don't bubble up
                // and stop the host; wait a bit before retrying.
                await Task.Delay(_fallbackJobPeriod, stoppingToken);
            }
#pragma warning restore ERP022
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Interlocked.Exchange(ref _started, 0);
        await base.StopAsync(cancellationToken);
    }
}
