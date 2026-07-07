// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Headless.AuditLog;
using Headless.Domain;
using Headless.EntityFramework.Contexts.Processors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.EntityFramework.Contexts.Runtime;

/// <summary>
/// Coordinates the per-<c>SaveChanges</c> work of a <see cref="HeadlessDbContext"/>: runs the ordered
/// chain of <see cref="IHeadlessSaveEntryProcessor"/> stages, captures audit entries, dispatches local
/// messages within the active transaction, persists the entity batch, and enqueues distributed messages
/// post-success before committing.
/// </summary>
/// <remarks>
/// Implementations own the transaction boundary. When an explicit transaction is already on the context
/// the pipeline reuses it; otherwise it opens a transaction wrapped by the execution strategy so audit and
/// message-emitter work commit atomically with the entity batch.
/// <para>
/// At-most-once domain-event delivery applies only to the pipeline-owned execution-strategy path (no explicit
/// transaction on entry): the guard suppresses re-publication across the strategy's transient-fault replays.
/// When the caller owns the transaction and drives its own retry loop, each <c>SaveChanges</c> is a fresh
/// invocation with a fresh guard, so domain-event handlers can fire again and must stay idempotent /
/// replay-safe. Integration events remain exactly-once via the transactional outbox regardless of path.
/// </para>
/// </remarks>
[PublicAPI]
public interface IHeadlessSaveChangesPipeline
{
    /// <summary>
    /// Asynchronously executes the full Headless save pipeline: runs processors, captures audit entries,
    /// dispatches domain events, persists the entity batch, enqueues integration events, and commits.
    /// </summary>
    /// <param name="context">The EF Core context being saved.</param>
    /// <param name="baseSaveChangesAsync">The base <c>SaveChangesAsync</c> delegate from the context.</param>
    /// <param name="acceptAllChangesOnSuccess">Whether to accept all changes on success.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(
        DbContext context,
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Synchronously executes the full Headless save pipeline.
    /// </summary>
    /// <param name="context">The EF Core context being saved.</param>
    /// <param name="baseSaveChanges">The base <c>SaveChanges</c> delegate from the context.</param>
    /// <param name="acceptAllChangesOnSuccess">Whether to accept all changes on success.</param>
    /// <returns>The number of state entries written to the database.</returns>
    int SaveChanges(DbContext context, Func<bool, int> baseSaveChanges, bool acceptAllChangesOnSuccess);
}

/// <summary>
/// Default <see cref="IHeadlessSaveChangesPipeline"/> implementation.
/// </summary>
/// <remarks>
/// <para>
/// Terminal-stage ordering: <see cref="HeadlessLocalEventSaveEntryProcessor"/> and
/// <see cref="HeadlessMessageCollectorSaveEntryProcessor"/> run last so consumer processors can mutate
/// entities before message-collection sees the final state.
/// </para>
/// <para>
/// Cancellation: <c>transaction.CommitAsync</c> has no implicit timeout beyond the supplied
/// <see cref="CancellationToken"/>. Callers should pass a deadline-bounded token when needed.
/// </para>
/// <para>
/// Design note: <see cref="Microsoft.EntityFrameworkCore.Diagnostics.ISaveChangesInterceptor"/> was
/// considered for this pipeline but rejected. The interceptor model cannot defer
/// <c>AcceptAllChanges</c> (the second <c>SaveChanges(false)</c> call needs deferred accept, controlled
/// by the caller via <c>acceptAllChangesOnSuccess: false</c>), and cannot compose ordered
/// <see cref="IHeadlessSaveEntryProcessor"/> stages with guaranteed terminal-stage placement. The
/// pipeline owns the explicit transaction boundary that interceptors don't expose cleanly.
/// </para>
/// </remarks>
internal sealed partial class HeadlessSaveChangesPipeline(
    IServiceProvider serviceProvider,
    HeadlessDbContextOptions options,
    IHeadlessAuditPersistence auditPersistence,
    ILocalEventBus? localEventBus = null,
    IHeadlessOutboxDispatcher? outboxDispatcher = null,
    ILogger<HeadlessSaveChangesPipeline>? logger = null
) : IHeadlessSaveChangesPipeline
{
    private const string _MissingLocalEventBusMessage =
        "Headless EF collected domain events to publish, but no ILocalEventBus is registered. "
        + "Call AddHeadlessDbContextServices(...).AddDomainEvents() (or services.AddHeadlessLocalEventBus()).";

    private const string _MissingOutboxDispatcherMessage =
        "Headless EF collected integration events to enqueue, but no IHeadlessOutboxDispatcher is registered. "
        + "Reference the Headless.Orm.EntityFramework.Messaging package and call "
        + "AddHeadlessDbContextServices(...).AddIntegrationEventOutbox().";

    private readonly IReadOnlyList<IHeadlessSaveEntryProcessor> _entryProcessors = options.ResolveSaveEntryProcessors(
        serviceProvider
    );

    private readonly ILogger<HeadlessSaveChangesPipeline> _logger =
        logger ?? NullLogger<HeadlessSaveChangesPipeline>.Instance;

    public async Task<int> SaveChangesAsync(
        DbContext context,
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken
    )
    {
        // Materialize once — the framework processors don't add new ChangeTracker entries during
        // _ProcessEntries, so a single snapshot is correct for the audit capture too.
        var trackedEntries = _SnapshotEntries(context);
        var saveContext = _ProcessEntries(context, trackedEntries);
        var auditEntries = auditPersistence.CaptureEntries(trackedEntries);

        var state = new AsyncSaveState(
            context,
            saveContext,
            auditEntries,
            acceptAllChangesOnSuccess,
            baseSaveChangesAsync,
            new StrongBox<bool>(),
            cancellationToken
        );

        if (context.Database.CurrentTransaction is not null)
        {
            return await _ExecuteWithinCurrentTransactionAsync(state).ConfigureAwait(false);
        }

        if (!_RequiresExplicitTransaction(auditEntries, saveContext))
        {
            var result = await baseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
            _CompleteSuccessfulSave(context, saveContext, default, acceptAllChangesOnSuccess);

            return result;
        }

        return await context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(state, _ExecuteWithNewTransactionAsync)
            .ConfigureAwait(false);
    }

    public int SaveChanges(DbContext context, Func<bool, int> baseSaveChanges, bool acceptAllChangesOnSuccess)
    {
#pragma warning disable MA0045 // Sync SaveChanges intentionally wraps EF sync APIs.
        var trackedEntries = _SnapshotEntries(context);
        var saveContext = _ProcessEntries(context, trackedEntries);
        var auditEntries = auditPersistence.CaptureEntries(trackedEntries);

        var state = new SaveState(
            context,
            saveContext,
            auditEntries,
            acceptAllChangesOnSuccess,
            baseSaveChanges,
            new StrongBox<bool>()
        );

        if (context.Database.CurrentTransaction is not null)
        {
            return _ExecuteWithinCurrentTransaction(state);
        }

        if (!_RequiresExplicitTransaction(auditEntries, saveContext))
        {
            var result = baseSaveChanges(acceptAllChangesOnSuccess);
            _CompleteSuccessfulSave(context, saveContext, default, acceptAllChangesOnSuccess);

            return result;
        }

        return context.Database.CreateExecutionStrategy().Execute(state, _ExecuteWithNewTransaction);
#pragma warning restore MA0045
    }

    private static EntityEntry[] _SnapshotEntries(DbContext context)
    {
        // Single allocation, single ChangeTracker traversal — feeds both _ProcessEntries and the
        // initial audit capture.
        return [.. context.ChangeTracker.Entries()];
    }

    private HeadlessSaveEntryContext _ProcessEntries(DbContext context, IReadOnlyList<EntityEntry> entries)
    {
        var tenantId = context is IHeadlessDbContext headlessContext ? headlessContext.TenantId : null;
        var saveContext = new HeadlessSaveEntryContext(context, tenantId);

        foreach (var entry in entries)
        {
            foreach (var processor in _entryProcessors)
            {
                processor.Process(entry, saveContext);
            }
        }

        return saveContext;
    }

    private Task<int> _ExecuteWithinCurrentTransactionAsync(AsyncSaveState state)
    {
        // CurrentTransaction was just verified non-null above; null-forgiving here documents that.
        var currentTransaction = state.Context.Database.CurrentTransaction!;
        return _SaveWithinTransactionAsync(state, currentTransaction, commitTransaction: false);
    }

    private int _ExecuteWithinCurrentTransaction(SaveState state)
    {
        // CurrentTransaction was just verified non-null above; null-forgiving here documents that.
        var currentTransaction = state.Context.Database.CurrentTransaction!;
        return _SaveWithinTransaction(state, currentTransaction, commitTransaction: false);
    }

    private async Task<int> _ExecuteWithNewTransactionAsync(AsyncSaveState state)
    {
        await using var transaction = await state
            .Context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, state.CancellationToken)
            .ConfigureAwait(false);

        // Enlist the open transaction in commit coordination SYNCHRONOUSLY, in this frame, so the ambient
        // coordinator (and its relational capability) flows to the dispatcher/outbox writer invoked inside the
        // save. The enlist must NOT live behind an async helper — an AsyncLocal push inside an async method does
        // not propagate back to this caller. The registered transaction interceptor drains enlisted work when the
        // transaction commits and discards it on rollback; disposing the scope is the un-signalled-dispose safety net.
        await using var coordination = state.Context.Database.EnlistCommitCoordination(
            transaction,
            serviceProvider,
            state.CancellationToken
        );

        return await _SaveWithinTransactionAsync(state, transaction, commitTransaction: true).ConfigureAwait(false);
    }

    private int _ExecuteWithNewTransaction(SaveState state)
    {
#pragma warning disable MA0045 // Sync intentionally
        // Sync twin of _ExecuteWithNewTransactionAsync — same open-then-synchronously-enlist shape.
        using var transaction = state.Context.Database.BeginTransaction(IsolationLevel.ReadCommitted);
        using var coordination = state.Context.Database.EnlistCommitCoordination(
            transaction,
            serviceProvider,
            CancellationToken.None
        );
        return _SaveWithinTransaction(state, transaction, commitTransaction: true);
#pragma warning restore MA0045
    }

    // Intentional sync/async twin of _SaveWithinTransaction below: identical save policy (at-most-once
    // domain-event loop, integration flatten+dispatch, audit capture, missing-bus/dispatcher guards). The two
    // are kept in lockstep by hand rather than extracted — any change here must be mirrored in the sync twin.
    private async Task<int> _SaveWithinTransactionAsync(
        AsyncSaveState state,
        IDbContextTransaction transaction,
        bool commitTransaction
    )
    {
        if (commitTransaction)
        {
            auditPersistence.PrepareForRetry(state.Context);
        }

        HeadlessAuditSaveResult auditSave = default;

        try
        {
            // Domain-event handlers are at-most-once. They run before baseSaveChanges so handlers can enlist
            // changes into this same save, but this method is the execution strategy's retried operation: a
            // transient fault during save/commit replays it. The DomainEventsPublished flag (shared across the
            // by-value state copies) ensures handlers are NOT re-invoked on a replay. The contract trade-off:
            // handlers may run even if a later attempt ultimately fails to commit, so domain-event side effects
            // must tolerate a rolled-back save (keep them idempotent / replay-safe). For commit-coupled,
            // exactly-once delivery use integration events (the transactional outbox), not domain events.
            if (!state.DomainEventsPublished.Value && state.SaveContext.DomainEventEmitters.Count > 0)
            {
                var bus = localEventBus ?? throw new InvalidOperationException(_MissingLocalEventBusMessage);

                foreach (var emitter in state.SaveContext.DomainEventEmitters)
                {
                    foreach (var domainEvent in emitter.Events)
                    {
                        state.CancellationToken.ThrowIfCancellationRequested();
                        await bus.PublishAsync(domainEvent, state.CancellationToken).ConfigureAwait(false);
                    }
                }

                state.DomainEventsPublished.Value = true;
            }

            var deferAcceptAllChanges = _HasAuditEntries(state.AuditEntries);
            var result = await state
                .BaseSaveChangesAsync(
                    !deferAcceptAllChanges && state.AcceptAllChangesOnSuccess,
                    state.CancellationToken
                )
                .ConfigureAwait(false);

            auditSave = await auditPersistence
                .ResolveAndPersistAsync(
                    state.Context,
                    state.AuditEntries,
                    state.BaseSaveChangesAsync,
                    state.CancellationToken
                )
                .ConfigureAwait(false);

            if (state.SaveContext.IntegrationEventEmitters.Count > 0)
            {
                var dispatcher =
                    outboxDispatcher ?? throw new InvalidOperationException(_MissingOutboxDispatcherMessage);

                var integrationEvents = state
                    .SaveContext.IntegrationEventEmitters.SelectMany(static emitter => emitter.Events)
                    .ToArray();

                await dispatcher.DispatchAsync(integrationEvents, state.CancellationToken).ConfigureAwait(false);
            }

            if (commitTransaction)
            {
                await transaction.CommitAsync(state.CancellationToken).ConfigureAwait(false);
            }

            _CompleteSuccessfulSave(state.Context, state.SaveContext, auditSave, state.AcceptAllChangesOnSuccess);

            return result;
        }
        catch (Exception caught)
        {
            try
            {
                auditPersistence.DiscardEntries(auditSave);
            }
#pragma warning disable CA1031 // Last-resort: a discard failure must not mask the original SaveChanges exception.
            catch (Exception discardFailure)
#pragma warning restore CA1031
            {
                _logger.LogAuditDiscardFailed(discardFailure);
            }

            ExceptionDispatchInfo.Capture(caught).Throw();
            throw; // unreachable; satisfies analyzers
        }
    }

    // Intentional sync/async twin of _SaveWithinTransactionAsync above: identical save policy (at-most-once
    // domain-event loop, integration flatten+dispatch, audit capture, missing-bus/dispatcher guards). The two
    // are kept in lockstep by hand rather than extracted — any change here must be mirrored in the async twin.
    private int _SaveWithinTransaction(SaveState state, IDbContextTransaction transaction, bool commitTransaction)
    {
#pragma warning disable MA0045 // Sync intentionally.
        if (commitTransaction)
        {
            auditPersistence.PrepareForRetry(state.Context);
        }

        HeadlessAuditSaveResult auditSave = default;

        try
        {
            // At-most-once domain-event guard — see the async twin for the full rationale. The shared
            // DomainEventsPublished flag prevents re-invoking handlers when the execution strategy replays
            // this operation after a transient save/commit fault.
            if (!state.DomainEventsPublished.Value && state.SaveContext.DomainEventEmitters.Count > 0)
            {
                var bus = localEventBus ?? throw new InvalidOperationException(_MissingLocalEventBusMessage);

                foreach (var emitter in state.SaveContext.DomainEventEmitters)
                {
                    foreach (var domainEvent in emitter.Events)
                    {
                        bus.Publish(domainEvent);
                    }
                }

                state.DomainEventsPublished.Value = true;
            }

            var deferAcceptAllChanges = _HasAuditEntries(state.AuditEntries);
            var result = state.BaseSaveChanges(!deferAcceptAllChanges && state.AcceptAllChangesOnSuccess);
            auditSave = auditPersistence.ResolveAndPersist(state.Context, state.AuditEntries, state.BaseSaveChanges);

            if (state.SaveContext.IntegrationEventEmitters.Count > 0)
            {
                var dispatcher =
                    outboxDispatcher ?? throw new InvalidOperationException(_MissingOutboxDispatcherMessage);

                var integrationEvents = state
                    .SaveContext.IntegrationEventEmitters.SelectMany(static emitter => emitter.Events)
                    .ToArray();

                dispatcher.Dispatch(integrationEvents);
            }

            if (commitTransaction)
            {
                transaction.Commit();
            }

            _CompleteSuccessfulSave(state.Context, state.SaveContext, auditSave, state.AcceptAllChangesOnSuccess);

            return result;
        }
        catch (Exception caught)
        {
            try
            {
                auditPersistence.DiscardEntries(auditSave);
            }
#pragma warning disable CA1031 // Last-resort: a discard failure must not mask the original SaveChanges exception.
            catch (Exception discardFailure)
#pragma warning restore CA1031
            {
                _logger.LogAuditDiscardFailed(discardFailure);
            }

            ExceptionDispatchInfo.Capture(caught).Throw();
            throw; // unreachable; satisfies analyzers
        }
#pragma warning restore MA0045
    }

    private static bool _RequiresExplicitTransaction(
        IReadOnlyList<AuditLogEntryData>? auditEntries,
        HeadlessSaveEntryContext saveContext
    ) =>
        _HasAuditEntries(auditEntries)
        || saveContext.IntegrationEventEmitters.Count > 0
        || saveContext.DomainEventEmitters.Count > 0;

    private static bool _HasAuditEntries(IReadOnlyList<AuditLogEntryData>? auditEntries)
    {
        return auditEntries is { Count: > 0 };
    }

    private void _CompleteSuccessfulSave(
        DbContext context,
        HeadlessSaveEntryContext saveContext,
        HeadlessAuditSaveResult auditSave,
        bool acceptAllChangesOnSuccess
    )
    {
        auditPersistence.CompleteSuccessfulSave(context, auditSave, acceptAllChangesOnSuccess);
        saveContext.ClearEmitterMessages();
    }

    private readonly record struct AsyncSaveState(
        DbContext Context,
        HeadlessSaveEntryContext SaveContext,
        IReadOnlyList<AuditLogEntryData>? AuditEntries,
        bool AcceptAllChangesOnSuccess,
        Func<bool, CancellationToken, Task<int>> BaseSaveChangesAsync,
        // Shared across the by-value state copies the execution strategy makes on retry, so the at-most-once
        // domain-event guard in _SaveWithinTransactionAsync survives a replay. See the publish loop there.
        StrongBox<bool> DomainEventsPublished,
        CancellationToken CancellationToken
    );

    private readonly record struct SaveState(
        DbContext Context,
        HeadlessSaveEntryContext SaveContext,
        IReadOnlyList<AuditLogEntryData>? AuditEntries,
        bool AcceptAllChangesOnSuccess,
        Func<bool, int> BaseSaveChanges,
        // Shared across the by-value state copies the execution strategy makes on retry, so the at-most-once
        // domain-event guard in _SaveWithinTransaction survives a replay. See the publish loop there.
        StrongBox<bool> DomainEventsPublished
    );
}

internal static partial class HeadlessSaveChangesPipelineLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "HeadlessAuditDiscardFailedDuringExceptionPath",
        Level = LogLevel.Error,
        Message = "Audit discard failed during exception path; rethrowing the original SaveChanges exception."
    )]
    public static partial void LogAuditDiscardFailed(this ILogger logger, Exception exception);
}
