// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Processor;

[PublicAPI]
public sealed class MessageProcessingServer(
    ILogger<MessageProcessingServer> logger,
    ILoggerFactory loggerFactory,
    IServiceProvider provider,
    TimeProvider timeProvider
) : IProcessingServer
{
    private CancellationTokenSource _cts = new();
    private readonly ILogger _logger = logger;

    private Task? _compositeTask;
    private ProcessingContext? _context;
    private bool _disposed;

    public ValueTask StartAsync(CancellationToken stoppingToken)
    {
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

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _disposed = true;

            _logger.ServerShuttingDown();
            await _cts.CancelAsync();

            if (_compositeTask is not null)
            {
                // #19 — thread the injected TimeProvider through WaitAsync so the shutdown grace
                // honors the test clock under FakeTimeProvider, and pass CancellationToken.None so
                // the wait observes only the timeout (the linked _cts has already been cancelled by
                // the CancelAsync above; if the composite tasks ignore it, falling back to the
                // ambient stoppingToken would re-cancel into the same wait pointlessly).
                await _compositeTask.WaitAsync(TimeSpan.FromSeconds(10), timeProvider, CancellationToken.None);
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
            _context?.Dispose();
            _context = null;
            _cts.Dispose();
            _logger.MessagingShutdown();
        }
    }
}
