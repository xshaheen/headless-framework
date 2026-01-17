// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Messages.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Framework.Messages.Processor;

public class CapProcessingServer(
    ILogger<CapProcessingServer> logger,
    ILoggerFactory loggerFactory,
    IServiceProvider provider
) : IProcessingServer
{
    private CancellationTokenSource _cts = new();
    private readonly ILogger _logger = logger;

    private Task? _compositeTask;
    private ProcessingContext _context = default!;
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
            if (!(innerEx is OperationCanceledException))
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
            _logger.LogInformation("### CAP shutdown!");
            GC.SuppressFinalize(this);
        }
    }

    private IProcessor _InfiniteRetry(IProcessor inner)
    {
        return new InfiniteRetryProcessor(inner, loggerFactory);
    }

    private IProcessor[] _GetProcessors()
    {
        var returnedProcessors = new List<IProcessor>
        {
            provider.GetRequiredService<TransportCheckProcessor>(),
            provider.GetRequiredService<MessageNeedToRetryProcessor>(),
            provider.GetRequiredService<MessageDelayedProcessor>(),
            provider.GetRequiredService<CollectorProcessor>(),
        };

        return returnedProcessors.ToArray();
    }
}
