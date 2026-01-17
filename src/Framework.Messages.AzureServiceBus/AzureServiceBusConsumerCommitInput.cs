// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;

namespace Framework.Messages;

public class AzureServiceBusConsumerCommitInput
{
    public AzureServiceBusConsumerCommitInput(ProcessMessageEventArgs processMessageEventArgs)
    {
        ProcessMessageArgs = processMessageEventArgs;
    }

    public AzureServiceBusConsumerCommitInput(ProcessSessionMessageEventArgs processSessionMessageArgs)
    {
        ProcessSessionMessageArgs = processSessionMessageArgs;
    }

    private ProcessMessageEventArgs? ProcessMessageArgs { get; }
    private ProcessSessionMessageEventArgs? ProcessSessionMessageArgs { get; }

    private ServiceBusReceivedMessage Message => ProcessMessageArgs?.Message ?? ProcessSessionMessageArgs!.Message;

    public Task CompleteMessageAsync()
    {
        return ProcessMessageArgs != null
            ? ProcessMessageArgs.CompleteMessageAsync(Message)
            : ProcessSessionMessageArgs!.CompleteMessageAsync(Message);
    }

    public Task AbandonMessageAsync()
    {
        return ProcessMessageArgs != null
            ? ProcessMessageArgs.AbandonMessageAsync(Message)
            : ProcessSessionMessageArgs!.AbandonMessageAsync(Message);
    }
}
