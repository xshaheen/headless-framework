// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.UnitOfWork;

namespace Headless.Messaging.Messages;

/// <summary>A context for consumers, it used to be provider wrapper of method description and received message.</summary>
/// <remarks>Create a new instance of  <see cref="ConsumerContext" /> .</remarks>
/// <param name="descriptor">consumer method descriptor. </param>
/// <param name="message"> received message.</param>
internal sealed class ConsumerContext(
    ConsumerExecutorDescriptor descriptor,
    MediumMessage message,
    IUnitOfWork? unitOfWork = null
)
{
    public ConsumerContext(ConsumerContext context)
        : this(context.ConsumerDescriptor, context.MediumMessage, context.UnitOfWork) { }

    /// <summary>The inbox transaction's unit on the transactional tier; null otherwise.</summary>
    public IUnitOfWork? UnitOfWork { get; } = unitOfWork;

    /// <summary>A descriptor of consumer information need to be performed.</summary>
    public ConsumerExecutorDescriptor ConsumerDescriptor { get; } = Argument.IsNotNull(descriptor);

    /// <summary>Consumer received medium message.</summary>
    public MediumMessage MediumMessage { get; } = Argument.IsNotNull(message);

    /// <summary>Consumer received message.</summary>
    public Message DeliverMessage => MediumMessage.Origin;
}
