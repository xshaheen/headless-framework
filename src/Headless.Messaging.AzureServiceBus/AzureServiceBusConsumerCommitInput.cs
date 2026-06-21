// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;

namespace Headless.Messaging.AzureServiceBus;

/// <summary>
/// Carries the Azure Service Bus settlement context needed to complete or abandon a received message.
/// Wraps either a <c>ProcessMessageEventArgs</c> (non-session) or a
/// <c>ProcessSessionMessageEventArgs</c> (session-enabled) transparently.
/// </summary>
public class AzureServiceBusConsumerCommitInput
{
    /// <summary>Initialises the input from a non-session message processing context.</summary>
    public AzureServiceBusConsumerCommitInput(ProcessMessageEventArgs processMessageEventArgs)
    {
        ProcessMessageArgs = processMessageEventArgs;
    }

    /// <summary>Initialises the input from a session-aware message processing context.</summary>
    public AzureServiceBusConsumerCommitInput(ProcessSessionMessageEventArgs processSessionMessageArgs)
    {
        ProcessSessionMessageArgs = processSessionMessageArgs;
    }

    private ProcessMessageEventArgs? ProcessMessageArgs { get; }
    private ProcessSessionMessageEventArgs? ProcessSessionMessageArgs { get; }

    private ServiceBusReceivedMessage Message => ProcessMessageArgs?.Message ?? ProcessSessionMessageArgs!.Message;

    /// <summary>Settles the message as successfully processed and removes it from the broker.</summary>
    public Task CompleteMessageAsync()
    {
        return ProcessMessageArgs != null
            ? ProcessMessageArgs.CompleteMessageAsync(Message)
            : ProcessSessionMessageArgs!.CompleteMessageAsync(Message);
    }

    /// <summary>
    /// Returns the message to the broker for redelivery. The message's delivery count is incremented
    /// and it becomes available to other consumers after the lock expires.
    /// </summary>
    public Task AbandonMessageAsync()
    {
        return ProcessMessageArgs != null
            ? ProcessMessageArgs.AbandonMessageAsync(Message)
            : ProcessSessionMessageArgs!.AbandonMessageAsync(Message);
    }
}
