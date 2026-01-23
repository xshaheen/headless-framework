// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Processor;

public sealed class MessageProcessingServer(
    ILogger<MessageProcessingServer> logger,
    ILoggerFactory loggerFactory,
    IServiceProvider provider
) : IProcessingServer
{
    private CancellationTokenSource _cts = new();
    private readonly ILogger _logger = logger;

    private Task? _compositeTask;
    private ProcessingContext? _context;
    private bool _disposed;

    public ValueTask StartAsync(CancellationToken stoppingToken)
    {
        // If already disposed and restarting, recreate the CancellationTokenSource
        if (_disposed || _cts.IsCancellationRequested)
        {
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _disposed = false;
        }

        stoppingToken.Register(() => _cts.Cancel());

        _logger.ServerStarting();

        _context = new ProcessingContext(provider, _cts.Token);

        var processorTasks = _GetProcessors().Select(_InfiniteRetry).Select(p => p.ProcessAsync(_context));
        _compositeTask = Task.WhenAll(processorTasks);

        return ValueTask.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _disposed = true;

            _logger.ServerShuttingDown();
            _cts.Cancel();

            _compositeTask?.Wait((int)TimeSpan.FromSeconds(10).TotalMilliseconds);
        }
        catch (AggregateException ex)
        {
            var innerEx = ex.InnerExceptions[0];
            if (innerEx is not OperationCanceledException)
            {
                _logger.ExpectedOperationCanceledException(innerEx);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "An exception was occurred when disposing.");
        }
        finally
        {
            _context?.Dispose();
            _context = null;
            _cts.Dispose();
            _logger.LogInformation("### Messaging system shutdown!");
        }
    }

    private IProcessor _InfiniteRetry(IProcessor inner)
    {
        return new InfiniteRetryProcessor(inner, loggerFactory);
    }

    private IProcessor[] _GetProcessors()
    {
        return
        [
            provider.GetRequiredService<TransportCheckProcessor>(),
            provider.GetRequiredService<MessageNeedToRetryProcessor>(),
            provider.GetRequiredService<MessageDelayedProcessor>(),
            provider.GetRequiredService<CollectorProcessor>(),
        ];
    }
}
