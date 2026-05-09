// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>A filter that surrounds a publish operation, symmetric to <see cref="IConsumeFilter"/>.</summary>
/// <remarks>
/// <para>
/// Filters run inside <c>IPublishExecutionPipeline</c> for every <see cref="IMessagePublisher.PublishAsync"/>
/// or <see cref="IScheduledPublisher.PublishDelayAsync"/> call. When multiple filters are registered,
/// the executing phase runs in registration order; the executed and exception phases run in reverse,
/// matching ASP.NET Core MVC filter pipeline semantics.
/// </para>
/// <para>
/// A filter that needs to carry state across the executing/executed/exception triad should keep that
/// state on its own instance fields, not on the context. The context types are sealed and do not
/// expose an <c>Items</c> bag.
/// </para>
/// </remarks>
public interface IPublishFilter
{
    /// <summary>Called before the message is handed to the publish request factory and the transport.</summary>
    /// <remarks>
    /// Filters may mutate <see cref="PublishFilterContext.Options"/> and
    /// <see cref="PublishFilterContext.DelayTime"/>. Mutations carry forward to subsequent filters in the
    /// chain and to the publish wrapper that ultimately calls <c>MessagePublishRequestFactory.Create</c>.
    /// </remarks>
    /// <param name="context">The <see cref="PublishingContext"/>.</param>
    ValueTask OnPublishExecutingAsync(PublishingContext context);

    /// <summary>Called after the publish operation completes successfully.</summary>
    /// <param name="context">The <see cref="PublishedContext"/>.</param>
    ValueTask OnPublishExecutedAsync(PublishedContext context);

    /// <summary>Called when the publish operation throws an <see cref="System.Exception"/>.</summary>
    /// <remarks>
    /// Setting <see cref="PublishExceptionContext.ExceptionHandled"/> to <see langword="true"/> swallows
    /// the exception and lets <see cref="IMessagePublisher.PublishAsync"/> return a successful task to the
    /// caller. This is **silent-swallow** semantics — the caller cannot distinguish a successful publish
    /// from a handled-exception failure. This differs from consume-side handling, which acks a message
    /// without misrepresenting success. Use with care.
    /// </remarks>
    /// <param name="context">The <see cref="PublishExceptionContext"/>.</param>
    ValueTask OnPublishExceptionAsync(PublishExceptionContext context);
}

/// <summary>Abstract base class for <see cref="IPublishFilter"/> for use when implementing a subset of the interface methods.</summary>
public abstract class PublishFilter : IPublishFilter
{
    /// <inheritdoc/>
    public virtual ValueTask OnPublishExecutingAsync(PublishingContext context)
    {
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask OnPublishExecutedAsync(PublishedContext context)
    {
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask OnPublishExceptionAsync(PublishExceptionContext context)
    {
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Carrier for state shared across the publish filter triad. <see cref="Options"/> and
/// <see cref="DelayTime"/> are mutable so a filter can set them in the executing phase
/// and have subsequent filters and the publish pipeline see the updated values.
/// </summary>
public abstract class PublishFilterContext
{
    /// <summary>Initializes a new instance of the <see cref="PublishFilterContext"/> class.</summary>
    /// <param name="content">The message payload being published. May be <see langword="null"/>.</param>
    /// <param name="messageType">The type of the message payload.</param>
    /// <param name="options">The current <see cref="PublishOptions"/>; mutated through the filter chain.</param>
    /// <param name="delayTime">
    /// The scheduled delay for delayed publishes (<see cref="IScheduledPublisher.PublishDelayAsync"/>);
    /// <see langword="null"/> for non-delayed publishes.
    /// </param>
    protected PublishFilterContext(object? content, Type messageType, PublishOptions? options, TimeSpan? delayTime)
    {
        Argument.IsNotNull(messageType);

        Content = content;
        MessageType = messageType;
        Options = options;
        DelayTime = delayTime;
    }

    /// <summary>Gets the message payload being published. May be <see langword="null"/>.</summary>
    public object? Content { get; }

    /// <summary>Gets the runtime type of the message payload.</summary>
    public Type MessageType { get; }

    /// <summary>
    /// Gets or sets the current <see cref="PublishOptions"/> for the publish operation.
    /// Filters may reassign this property — typically via a record <c>with</c> expression
    /// such as <c>context.Options = (context.Options ?? new()) with { TenantId = "..." }</c>.
    /// The final value is consumed by <c>MessagePublishRequestFactory.Create</c>.
    /// </summary>
    public PublishOptions? Options { get; set; }

    /// <summary>
    /// Gets or sets the scheduled delay for the publish operation.
    /// <see langword="null"/> for non-delayed publishes; non-null when published via
    /// <see cref="IScheduledPublisher.PublishDelayAsync"/>. Filters may reassign this property
    /// to extend, shorten, or remove the delay before the publish wrapper acts on it.
    /// </summary>
    /// <remarks>
    /// Mutating this property only takes effect when the publisher entry point is delay-aware.
    /// Setting <see cref="DelayTime"/> from a filter during an immediate
    /// <see cref="IMessagePublisher.PublishAsync"/> call is silently ignored — the publisher
    /// chose the immediate path and will not promote the message to delayed delivery.
    /// </remarks>
    public TimeSpan? DelayTime { get; set; }
}

/// <summary>
/// Pre-publish filter context. Filters should mutate <see cref="PublishFilterContext.Options"/>
/// and <see cref="PublishFilterContext.DelayTime"/> here.
/// </summary>
public sealed class PublishingContext : PublishFilterContext
{
    /// <summary>Initializes a new instance of the <see cref="PublishingContext"/> class.</summary>
    public PublishingContext(object? content, Type messageType, PublishOptions? options, TimeSpan? delayTime)
        : base(content, messageType, options, delayTime) { }
}

/// <summary>Post-success filter context. Invoked once the transport or outbox storage accepted the message.</summary>
public sealed class PublishedContext : PublishFilterContext
{
    /// <summary>Initializes a new instance of the <see cref="PublishedContext"/> class.</summary>
    public PublishedContext(object? content, Type messageType, PublishOptions? options, TimeSpan? delayTime)
        : base(content, messageType, options, delayTime) { }
}

/// <summary>
/// Exception filter context. Invoked when the publish operation throws.
/// </summary>
/// <remarks>
/// Setting <see cref="ExceptionHandled"/> to <see langword="true"/> swallows the exception and lets
/// <see cref="IMessagePublisher.PublishAsync"/> return a successful task to the caller. This is
/// **silent-swallow** semantics — the caller cannot tell the publish failed. Filter authors should
/// set this only when the failure is provably non-fatal at the application boundary; otherwise leave
/// it <see langword="false"/> and let the exception propagate.
/// </remarks>
public sealed class PublishExceptionContext : PublishFilterContext
{
    /// <summary>Initializes a new instance of the <see cref="PublishExceptionContext"/> class.</summary>
    public PublishExceptionContext(
        object? content,
        Type messageType,
        PublishOptions? options,
        TimeSpan? delayTime,
        Exception exception
    )
        : base(content, messageType, options, delayTime)
    {
        Argument.IsNotNull(exception);

        Exception = exception;
    }

    /// <summary>Gets the exception thrown by the publish operation.</summary>
    public Exception Exception { get; }

    /// <summary>
    /// Gets or sets whether the exception was handled. When <see langword="true"/>, the publish
    /// pipeline returns a successful task to the caller and the exception is not rethrown.
    /// </summary>
    public bool ExceptionHandled { get; set; }
}
