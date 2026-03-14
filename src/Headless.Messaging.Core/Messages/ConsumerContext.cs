// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Messages;

/// <summary>A context for consumers, it used to be provider wrapper of method description and received message.</summary>
/// <remarks>Create a new instance of  <see cref="ConsumerContext" /> .</remarks>
/// <param name="descriptor">consumer method descriptor. </param>
/// <param name="message"> received message.</param>
public class ConsumerContext(ConsumerExecutorDescriptor descriptor, MediumMessage message)
{
    public ConsumerContext(ConsumerContext context)
        : this(context.ConsumerDescriptor, context.MediumMessage) { }

    /// <summary>A descriptor of consumer information need to be performed.</summary>
    public ConsumerExecutorDescriptor ConsumerDescriptor { get; } = Argument.IsNotNull(descriptor);

    /// <summary>Consumer received medium message.</summary>
    public MediumMessage MediumMessage { get; } = Argument.IsNotNull(message);

    /// <summary>Consumer received message.</summary>
    public Message DeliverMessage => MediumMessage.Origin;
}
