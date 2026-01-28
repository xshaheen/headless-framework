// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;

namespace Headless.Messaging.AzureServiceBus;

public class ServiceBusProcessorFacade : IAsyncDisposable
{
    private readonly ServiceBusProcessor? _serviceBusProcessor;
    private readonly ServiceBusSessionProcessor? _serviceBusSessionProcessor;

    public bool IsSessionProcessor { get; }

    public bool IsProcessing =>
        IsSessionProcessor ? _serviceBusSessionProcessor!.IsProcessing : _serviceBusProcessor!.IsProcessing;

    public bool AutoCompleteMessages =>
        IsSessionProcessor
            ? _serviceBusSessionProcessor!.AutoCompleteMessages
            : _serviceBusProcessor!.AutoCompleteMessages;

    public ServiceBusProcessorFacade(
        ServiceBusProcessor? serviceBusProcessor = null,
        ServiceBusSessionProcessor? serviceBusSessionProcessor = null
    )
    {
        if (serviceBusProcessor is null && serviceBusSessionProcessor is null)
        {
            throw new ArgumentNullException(
                nameof(serviceBusProcessor),
                @"Either serviceBusProcessor or serviceBusSessionProcessor must be provided"
            );
        }

        _serviceBusProcessor = serviceBusProcessor;
        _serviceBusSessionProcessor = serviceBusSessionProcessor;

        IsSessionProcessor = _serviceBusSessionProcessor is not null;
    }

    public Task StartProcessingAsync(CancellationToken cancellationToken = default)
    {
        return IsSessionProcessor
            ? _serviceBusSessionProcessor!.StartProcessingAsync(cancellationToken)
            : _serviceBusProcessor!.StartProcessingAsync(cancellationToken);
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

#pragma warning disable CA1816
    // CA1816: Class is not sealed; SuppressFinalize helps derived types avoid re-implementing IDisposable
    public async ValueTask DisposeAsync()
#pragma warning restore CA1816
    {
        if (_serviceBusProcessor is not null)
        {
            await _serviceBusProcessor.DisposeAsync();
        }

        if (_serviceBusSessionProcessor is not null)
        {
            await _serviceBusSessionProcessor.DisposeAsync();
        }
    }
}
