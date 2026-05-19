// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging;

/// <summary>Middleware that wraps a publish operation.</summary>
/// <typeparam name="TContext">The publish context type this middleware handles.</typeparam>
[PublicAPI]
public interface IPublishMiddleware<in TContext>
    where TContext : PublishContext
{
    /// <summary>Invokes this middleware and optionally calls the next middleware or publisher in the chain.</summary>
    ValueTask InvokeAsync(TContext context, Func<ValueTask> next);
}
