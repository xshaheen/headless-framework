// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>Middleware that wraps a consume operation.</summary>
/// <typeparam name="TContext">The consume context type this middleware handles.</typeparam>
[PublicAPI]
public interface IConsumeMiddleware<in TContext>
    where TContext : ConsumeContext
{
    /// <summary>Invokes this middleware and optionally calls the next middleware or consumer in the chain.</summary>
    ValueTask InvokeAsync(TContext context, Func<ValueTask> next);
}
