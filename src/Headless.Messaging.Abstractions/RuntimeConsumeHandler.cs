// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>
/// Represents a runtime message handler that executes inside the same scoped consume pipeline as class-based handlers.
/// </summary>
/// <typeparam name="TMessage">The message type handled by the delegate.</typeparam>
/// <param name="context">The typed consume context for the current message.</param>
/// <param name="services">The scoped services for the current execution.</param>
/// <param name="cancellationToken">The cancellation token for the current execution.</param>
[PublicAPI]
public delegate ValueTask RuntimeConsumeHandler<TMessage>(
    ConsumeContext<TMessage> context,
    IServiceProvider services,
    CancellationToken cancellationToken
)
    where TMessage : class;
