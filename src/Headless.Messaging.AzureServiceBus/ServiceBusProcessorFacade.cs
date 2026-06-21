// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;

namespace Headless.Messaging.AzureServiceBus;

/// <summary>
/// Unifies <c>ServiceBusProcessor</c> and <c>ServiceBusSessionProcessor</c> behind a single
/// interface so the consumer client can start, stop, and subscribe to events without branching
/// on whether sessions are enabled.
/// </summary>
/// <remarks>
/// Exactly one of <c>serviceBusProcessor</c> or <c>serviceBusSessionProcessor</c> must be provided
/// at construction. Passing neither throws <see cref="ArgumentNullException"/>.
/// </remarks>
public sealed class ServiceBusProcessorFacade : IAsyncDisposable
{
    private readonly ServiceBusProcessor? _serviceBusProcessor;
    private readonly ServiceBusSessionProcessor? _serviceBusSessionProcessor;

    /// <summary>
    /// <see langword="true"/> when the underlying processor is a session-aware
    /// <c>ServiceBusSessionProcessor</c>; <see langword="false"/> for a standard processor.
    /// </summary>
    public bool IsSessionProcessor { get; }

    /// <summary>
    /// <see langword="true"/> when the underlying processor is currently running and accepting messages.
    /// </summary>
    public bool IsProcessing =>
        IsSessionProcessor ? _serviceBusSessionProcessor!.IsProcessing : _serviceBusProcessor!.IsProcessing;

    /// <summary>
    /// <see langword="true"/> when the processor automatically completes messages after the handler returns.
    /// </summary>
    public bool AutoCompleteMessages =>
        IsSessionProcessor
            ? _serviceBusSessionProcessor!.AutoCompleteMessages
            : _serviceBusProcessor!.AutoCompleteMessages;

    /// <summary>
    /// Initialises the facade with either a standard or a session-aware processor.
    /// </summary>
    /// <param name="serviceBusProcessor">A non-session processor, or <see langword="null"/>.</param>
    /// <param name="serviceBusSessionProcessor">A session-aware processor, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Both parameters are <see langword="null"/>.</exception>
    public ServiceBusProcessorFacade(
        ServiceBusProcessor? serviceBusProcessor = null,
        ServiceBusSessionProcessor? serviceBusSessionProcessor = null
    )
    {
        if (serviceBusProcessor is null && serviceBusSessionProcessor is null)
        {
            throw new ArgumentNullException(
                nameof(serviceBusProcessor),
                "Either serviceBusProcessor or serviceBusSessionProcessor must be provided"
            );
        }

        _serviceBusProcessor = serviceBusProcessor;
        _serviceBusSessionProcessor = serviceBusSessionProcessor;

        IsSessionProcessor = _serviceBusSessionProcessor is not null;
    }

    /// <summary>Starts the underlying processor so it begins receiving messages.</summary>
    public Task StartProcessingAsync(CancellationToken cancellationToken = default)
    {
        return IsSessionProcessor
            ? _serviceBusSessionProcessor!.StartProcessingAsync(cancellationToken)
            : _serviceBusProcessor!.StartProcessingAsync(cancellationToken);
    }

    /// <summary>Stops the underlying processor from receiving new messages.</summary>
    public Task StopProcessingAsync(CancellationToken cancellationToken = default)
    {
        return IsSessionProcessor
            ? _serviceBusSessionProcessor!.StopProcessingAsync(cancellationToken)
            : _serviceBusProcessor!.StopProcessingAsync(cancellationToken);
    }

#pragma warning disable CA1003, MA0046
    // CA1003/MA0046: Must use Func<T, Task> to match Azure SDK's ServiceBusProcessor event signatures
    public event Func<ProcessMessageEventArgs, Task> ProcessMessageAsync
    {
        add => _serviceBusProcessor!.ProcessMessageAsync += value;
        remove => _serviceBusProcessor!.ProcessMessageAsync -= value;
    }

    public event Func<ProcessSessionMessageEventArgs, Task> ProcessSessionMessageAsync
    {
        add => _serviceBusSessionProcessor!.ProcessMessageAsync += value;
        remove => _serviceBusSessionProcessor!.ProcessMessageAsync -= value;
    }

    public event Func<ProcessErrorEventArgs, Task> ProcessErrorAsync
    {
        add
        {
            if (IsSessionProcessor)
            {
                _serviceBusSessionProcessor!.ProcessErrorAsync += value;
            }
            else
            {
                _serviceBusProcessor!.ProcessErrorAsync += value;
            }
        }
        remove
        {
            if (IsSessionProcessor)
            {
                _serviceBusSessionProcessor!.ProcessErrorAsync -= value;
            }
            else
            {
                _serviceBusProcessor!.ProcessErrorAsync -= value;
            }
        }
    }
#pragma warning restore CA1003, MA0046

    public async ValueTask DisposeAsync()
    {
        if (_serviceBusProcessor is not null)
        {
            await _serviceBusProcessor.DisposeAsync().ConfigureAwait(false);
        }

        if (_serviceBusSessionProcessor is not null)
        {
            await _serviceBusSessionProcessor.DisposeAsync().ConfigureAwait(false);
        }
    }
}
