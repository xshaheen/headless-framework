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
    private Task? _quiesceTask;
    private IReadOnlyCollection<Task>? _retryTasks;
    internal Action? StartPublicationHookForTest { get; set; }

    public ValueTask StartAsync(CancellationToken stoppingToken)
    {
        lock (_lifecycleLock)
        {
            if (_eventualCleanupTask is { IsCompleted: false })
            {
                throw new InvalidOperationException("Message processor shutdown is still in progress.");
            }

            if (_quiesceTask is not null && _eventualCleanupTask is null)
            {
                throw new InvalidOperationException("Message processor shutdown is still in progress.");
            }

            _eventualCleanupTask = null;
            _quiesceTask = null;
            _retryTasks = null;
            StartPublicationHookForTest?.Invoke();

            // Publish the complete generation while holding the same gate as Quiesce. Shutdown can
            // therefore observe either the previous generation or this one, never a partially reset CTS.
            var prior = _cts;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            prior.Dispose();

            _logger.ServerStarting();

            _context = new ProcessingContext(provider, timeProvider, _cts.Token);
            _retryProcessor.StartRun();

            var processorTasks = _GetProcessors().Select(_InfiniteRetry).Select(p => p.ProcessAsync(_context));
            _compositeTask = Task.WhenAll(processorTasks);
        }

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

    void IProcessingServerShutdown.Quiesce()
    {
        _Quiesce();
    }

    ValueTask IProcessingServerShutdown.StopAsync(TimeSpan timeout)
    {
        return _StopAsync(timeout);
    }

    private async ValueTask _StopAsync(TimeSpan timeout)
    {
        _Quiesce();

        Task cleanupTask;
        lock (_lifecycleLock)
        {
            _eventualCleanupTask ??= _CompleteShutdownAsync(_retryTasks ?? [], _quiesceTask ?? Task.CompletedTask);

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

    private void _Quiesce()
    {
        lock (_lifecycleLock)
        {
            _retryTasks ??= _retryProcessor.Quiesce();
            _quiesceTask ??= _cts.CancelAsync();
        }
    }

    private async Task _CompleteShutdownAsync(IReadOnlyCollection<Task> retryTasks, Task quiesceTask)
    {
        try
        {
            _logger.ServerShuttingDown();
#pragma warning disable VSTHRD003 // Quiesce starts this generation-owned CancelAsync task before drain begins.
            await quiesceTask.ConfigureAwait(false);
#pragma warning restore VSTHRD003

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
