// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging;

/// <summary>A filter that surrounds a publish operation, symmetric to <see cref="IConsumeFilter"/>.</summary>
/// <remarks>
/// <para>
/// Filters run inside the publish pipeline for every <see cref="IMessagePublisher.PublishAsync"/>
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
    /// <param name="cancellationToken">Cancellation token threaded from the outer publish call.</param>
    ValueTask OnPublishExecutingAsync(PublishingContext context, CancellationToken cancellationToken = default);

    /// <summary>Called after the publish operation completes successfully.</summary>
    /// <remarks>
    /// Exceptions thrown from this phase are logged and suppressed. At this point the message was already
    /// accepted by the transport or outbox, so surfacing a post-success filter failure to the caller would
    /// invite retries that can duplicate the message.
    /// </remarks>
    /// <param name="context">The <see cref="PublishedContext"/>.</param>
    /// <param name="cancellationToken">Cancellation token threaded from the outer publish call.</param>
    ValueTask OnPublishExecutedAsync(PublishedContext context, CancellationToken cancellationToken = default);

    /// <summary>Called when the publish operation throws an <see cref="System.Exception"/>.</summary>
    /// <remarks>
    /// Setting <see cref="PublishExceptionContext.ExceptionHandled"/> to <see langword="true"/> swallows
    /// the exception and lets <see cref="IMessagePublisher.PublishAsync"/> return a successful task to the
    /// caller. This is **silent-swallow** semantics — the caller cannot distinguish a successful publish
    /// from a handled-exception failure. This differs from consume-side handling, which acks a message
    /// without misrepresenting success. Use with care.
    /// </remarks>
    /// <param name="context">The <see cref="PublishExceptionContext"/>.</param>
    /// <param name="cancellationToken">Cancellation token threaded from the outer publish call.</param>
    ValueTask OnPublishExceptionAsync(PublishExceptionContext context, CancellationToken cancellationToken = default);
}

/// <summary>Abstract base class for <see cref="IPublishFilter"/> for use when implementing a subset of the interface methods.</summary>
public abstract class PublishFilter : IPublishFilter
{
    /// <inheritdoc/>
    public virtual ValueTask OnPublishExecutingAsync(
        PublishingContext context,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask OnPublishExecutedAsync(
        PublishedContext context,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc/>
    public virtual ValueTask OnPublishExceptionAsync(
        PublishExceptionContext context,
        CancellationToken cancellationToken = default
    )
    {
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Carrier for state shared across the publish filter triad. <see cref="Options"/> and
/// <see cref="DelayTime"/> are read-only on this base — only the executing-phase context
/// (<see cref="PublishingContext"/>) re-exposes them as mutable so filters can carry forward
/// reassignments to subsequent filters and the publish pipeline.
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
        OptionsCore = options;
        DelayTimeCore = delayTime;
    }

    /// <summary>Gets the message payload being published. May be <see langword="null"/>.</summary>
    public object? Content { get; }

    /// <summary>Gets the runtime type of the message payload.</summary>
    public Type MessageType { get; }

    /// <summary>
    /// Gets the current <see cref="PublishOptions"/> for the publish operation.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mutable only on <see cref="PublishingContext"/>. Filters that need to reassign
    /// <see cref="PublishOptions"/> — typically via a record <c>with</c> expression such as
    /// <c>context.Options = (context.Options ?? new()) with { TenantId = "..." }</c> — must do so
    /// during the executing phase. The final value is consumed by
    /// <c>MessagePublishRequestFactory.Create</c>.
    /// </para>
    /// <para>
    /// Read-only on <see cref="PublishedContext"/> and <see cref="PublishExceptionContext"/>: the
    /// publish has already happened and a new value would not flow to the transport, the outbox,
    /// or to subsequent filters.
    /// </para>
    /// </remarks>
    public PublishOptions? Options => OptionsCore;

    /// <summary>
    /// Gets the scheduled delay for the publish operation.
    /// <see langword="null"/> for non-delayed publishes; non-null when published via
    /// <see cref="IScheduledPublisher.PublishDelayAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mutable only on <see cref="PublishingContext"/>. Filters may reassign this property during
    /// the executing phase to extend, shorten, or remove the delay before the publish wrapper acts
    /// on it. Setting <see cref="DelayTime"/> from a filter during an immediate
    /// <see cref="IMessagePublisher.PublishAsync"/> call is silently ignored — the publisher
    /// chose the immediate path and will not promote the message to delayed delivery.
    /// </para>
    /// <para>
    /// Read-only on <see cref="PublishedContext"/> and <see cref="PublishExceptionContext"/>:
    /// the schedule decision is final once the publish has completed.
    /// </para>
    /// </remarks>
    public TimeSpan? DelayTime => DelayTimeCore;

    /// <summary>
    /// Backing storage for <see cref="Options"/>; protected so the executing-phase context can
    /// re-expose a public setter without duplicating state.
    /// </summary>
    protected PublishOptions? OptionsCore { get; set; }

    /// <summary>
    /// Backing storage for <see cref="DelayTime"/>; protected so the executing-phase context can
    /// re-expose a public setter without duplicating state.
    /// </summary>
    protected TimeSpan? DelayTimeCore { get; set; }
}

/// <summary>
/// Pre-publish filter context. Filters should mutate <see cref="Options"/> and
/// <see cref="DelayTime"/> here.
/// </summary>
public sealed class PublishingContext : PublishFilterContext
{
    /// <summary>Initializes a new instance of the <see cref="PublishingContext"/> class.</summary>
    public PublishingContext(object? content, Type messageType, PublishOptions? options, TimeSpan? delayTime)
        : base(content, messageType, options, delayTime) { }

    /// <summary>
    /// Gets or sets the current <see cref="PublishOptions"/> for the publish operation.
    /// Filters may reassign this property — typically via a record <c>with</c> expression
    /// such as <c>context.Options = (context.Options ?? new()) with { TenantId = "..." }</c>.
    /// The final value is consumed by <c>MessagePublishRequestFactory.Create</c>.
    /// </summary>
    /// <remarks>
    /// Assigning <see langword="null"/> here discards every caller-set field on the previous
    /// <see cref="PublishOptions"/> (including <c>MessageId</c>, <c>CorrelationId</c>, <c>TenantId</c>,
    /// <c>Topic</c>, <c>CallbackName</c>, and custom <c>Headers</c>). To mutate a single field while
    /// preserving the others, use a record <c>with</c> expression on the existing instance:
    /// <c>context.Options = (context.Options ?? new()) with { Field = newValue }</c>.
    /// </remarks>
    public new PublishOptions? Options
    {
        get => OptionsCore;
        set => OptionsCore = value;
    }

    /// <summary>
    /// Gets or sets the scheduled delay for the publish operation. Filters may reassign this
    /// property to extend, shorten, or remove the delay before the publish wrapper acts on it.
    /// </summary>
    public new TimeSpan? DelayTime
    {
        get => DelayTimeCore;
        set => DelayTimeCore = value;
    }
}

/// <summary>Post-success filter context. Invoked once the transport or outbox storage accepted the message.</summary>
/// <remarks>
/// <see cref="PublishFilterContext.Options"/> and <see cref="PublishFilterContext.DelayTime"/>
/// are read-only here. The publish has already completed; reassigning would not flow downstream.
/// </remarks>
public sealed class PublishedContext : PublishFilterContext
{
    /// <summary>Initializes a new instance of the <see cref="PublishedContext"/> class.</summary>
    public PublishedContext(object? content, Type messageType, PublishOptions? options, TimeSpan? delayTime)
        : base(content, messageType, options, delayTime) { }

    /// <summary>
    /// Gets a value indicating whether the publish was committed inside an ambient outbox
    /// transaction whose commit is the caller's responsibility (non-AutoCommit branch).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see langword="true"/> only on the <c>OutboxPublisher</c> non-AutoCommit branch — the
    /// message has been buffered into the outbox under the caller's database transaction and
    /// will only become visible to the dispatcher once the caller commits. A rollback discards
    /// the message.
    /// </para>
    /// <para>
    /// <see langword="false"/> on <c>DirectPublisher</c> (already on the wire) and on the
    /// <c>OutboxPublisher</c> AutoCommit branch (already committed inside the publisher).
    /// </para>
    /// <para>
    /// Filters that record durable side-effects in <see cref="IPublishFilter.OnPublishExecutedAsync"/>
    /// can check this flag and either skip work, enroll in the ambient transaction, or design
    /// the side-effect to be idempotent against rollback — surfacing the transactional boundary
    /// as a typed contract rather than an undocumented gotcha.
    /// </para>
    /// </remarks>
    public bool IsTransactional { get; init; }
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
