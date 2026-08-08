// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Processor;

internal sealed class MessageProcessingServer(
    ILogger<MessageProcessingServer> logger,
    ILoggerFactory loggerFactory,
    IServiceProvider provider,
    TimeProvider timeProvider
) : IProcessingServer, IProcessingServerShutdown
{
    private readonly Lock _lifecycleLock = new();
    private CancellationTokenSource _cts = new();
    private readonly ILogger _logger = logger;
    private readonly MessageNeedToRetryProcessor _retryProcessor =
        provider.GetRequiredService<MessageNeedToRetryProcessor>();
    private readonly TimeSpan _shutdownTimeout = provider
        .GetRequiredService<IOptions<MessagingOptions>>()
        .Value.ShutdownTimeout;

    private Task? _compositeTask;
    private ProcessingContext? _context;
    private Task? _eventualCleanupTask;
    private bool _disposed;

    public ValueTask StartAsync(CancellationToken stoppingToken)
    {
        lock (_lifecycleLock)
        {
            if (_eventualCleanupTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("Message processor shutdown is still in progress.");
            }

            _eventualCleanupTask = null;
        }

        // If already disposed and restarting, recreate the CancellationTokenSource so it's linked
        // to the freshly supplied stoppingToken. The previous CTS (which may have already fired)
        // is disposed first.
        if (_disposed || _cts.IsCancellationRequested)
        {
            _cts.Dispose();
            _cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _disposed = false;
        }
        else
        {
            // First start path: replace the parameterless CTS allocated at field init with a linked
            // one so stoppingToken propagation does not depend on the discarded
            // `stoppingToken.Register(...)` registration (which leaked the IDisposable). Linking the
            // outer token at construction time is both leak-free and dispose-safe across restarts.
            var prior = _cts;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            prior.Dispose();
        }

        _logger.ServerStarting();

        _context = new ProcessingContext(provider, timeProvider, _cts.Token);
        _retryProcessor.StartRun();

        var processorTasks = _GetProcessors().Select(_InfiniteRetry).Select(p => p.ProcessAsync(_context));
        _compositeTask = Task.WhenAll(processorTasks);

        return ValueTask.CompletedTask;
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

    public ValueTask DisposeAsync()
    {
        return _StopAsync(_shutdownTimeout);
    }

    ValueTask IProcessingServerShutdown.StopAsync(TimeSpan timeout)
    {
        return _StopAsync(timeout);
    }

    private async ValueTask _StopAsync(TimeSpan timeout)
    {
        Task cleanupTask;
        lock (_lifecycleLock)
        {
            if (_eventualCleanupTask is null)
            {
                _disposed = true;
                var retryTasks = _retryProcessor.Quiesce();
                _eventualCleanupTask = _CompleteShutdownAsync(retryTasks);
            }

            cleanupTask = _eventualCleanupTask;
        }

        if (cleanupTask.IsCompleted)
        {
            await cleanupTask.ConfigureAwait(false);
            return;
        }

        if (timeout <= TimeSpan.Zero)
        {
            _logger.DisposingWarning(new TimeoutException("The shared messaging shutdown deadline has expired."));
            return;
        }

        try
        {
            await cleanupTask.WaitAsync(timeout, timeProvider, CancellationToken.None).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            _logger.DisposingWarning(ex);
        }
    }

    private async Task _CompleteShutdownAsync(IReadOnlyCollection<Task> retryTasks)
    {
        try
        {
            _logger.ServerShuttingDown();
            await _cts.CancelAsync().ConfigureAwait(false);

            var tasks = new List<Task>(retryTasks.Count + 1);
            tasks.AddRange(retryTasks);
            if (_compositeTask is { } compositeTask)
            {
                tasks.Add(compositeTask);
            }

            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
        }
        catch (AggregateException e)
        {
            var inner = e.InnerExceptions[0];
            if (inner is not OperationCanceledException)
            {
                _logger.ExpectedOperationCanceledException(inner, inner.Message);
            }
        }
        catch (Exception e)
        {
            _logger.DisposingWarning(e);
        }
        finally
        {
            if (_context is not null)
            {
                await _context.DisposeAsync().ConfigureAwait(false);
            }
            _context = null;
            _cts.Dispose();
            _logger.MessagingShutdown();
        }
    }
}
