// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Messages;

namespace Headless.Messaging;

/// <summary>A filter that surrounds execution of the subscriber.</summary>
/// <remarks>
/// When multiple filters are registered via <see cref="Configuration.MessagingBuilder.AddSubscribeFilter{T}"/>,
/// they form a pipeline: the executing phase runs in registration order; the executed and exception phases
/// run in reverse, matching ASP.NET Core MVC filter pipeline semantics. Filters share a single
/// <see cref="ExecutingContext"/>, <see cref="ExecutedContext"/>, and <see cref="ExceptionContext"/>
/// instance per phase, so a downstream filter sees mutations applied by upstream filters.
/// </remarks>
public interface IConsumeFilter
{
    /// <summary>Called before the subscriber executes.</summary>
    /// <param name="context">The <see cref="ExecutingContext" />.</param>
    ValueTask OnSubscribeExecutingAsync(ExecutingContext context);

    /// <summary>Called after the subscriber executes.</summary>
    /// <param name="context">The <see cref="ExecutedContext" />.</param>
    ValueTask OnSubscribeExecutedAsync(ExecutedContext context);

    /// <summary>Called after the subscriber has thrown an <see cref="System.Exception" />.</summary>
    /// <remarks>
    /// Setting <see cref="ExceptionContext.ExceptionHandled"/> to <see langword="true"/> swallows the
    /// exception and prevents it from propagating out of the consume pipeline. When multiple filters
    /// are present, <see cref="ExceptionContext.ExceptionHandled"/> persists across the reverse-order
    /// exception chain — a downstream filter that sets it to <see langword="false"/> can re-arm
    /// propagation, but the typical pattern is upstream filters reading the flag set by inner filters.
    /// </remarks>
    /// <param name="context">The <see cref="ExceptionContext" />.</param>
    ValueTask OnSubscribeExceptionAsync(ExceptionContext context);
}

/// <summary>Abstract base class for ISubscribeFilter for use when implementing a subset of the interface methods.</summary>
public abstract class ConsumeFilter : IConsumeFilter
{
    /// <inheritdoc/>
    public virtual ValueTask OnSubscribeExecutingAsync(ExecutingContext context)
    {
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask OnSubscribeExecutedAsync(ExecutedContext context)
    {
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask OnSubscribeExceptionAsync(ExceptionContext context)
    {
        return ValueTask.CompletedTask;
    }
}

public class FilterContext(ConsumerContext context) : ConsumerContext(context);

public sealed class ExecutingContext(ConsumerContext context, object?[] arguments) : FilterContext(context)
{
    public object?[] Arguments { get; init; } = arguments;
}

public sealed class ExecutedContext(ConsumerContext context, object? result) : FilterContext(context)
{
    /// <summary>Gets or sets the result returned by the subscriber. Filters may mutate this value.</summary>
    public object? Result { get; set; } = result;
}

public sealed class ExceptionContext(ConsumerContext context, Exception exception) : FilterContext(context)
{
    /// <summary>Gets the exception thrown by the subscriber.</summary>
    public Exception Exception { get; init; } = exception;

    /// <summary>
    /// Gets or sets whether the exception was handled. When set <see langword="true"/> by any filter,
    /// the consume pipeline does not rethrow the exception.
    /// </summary>
    public bool ExceptionHandled { get; set; }

    /// <summary>Gets or sets a fallback result to surface when the exception is handled.</summary>
    public object? Result { get; set; }
}
