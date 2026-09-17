// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Headless.AuditLog;
using Headless.Domain;
using Headless.EntityFramework.Contexts.Processors;
using Headless.UnitOfWork;
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
/// the pipeline reuses it; otherwise it opens a transaction wrapped by the execution strategy, enlists it in a
/// unit of work for the save's duration, and completes that unit after the commit so audit and message-emitter
/// work commits atomically with the entity batch and after-commit work drains once the outcome is durable.
/// <para>
/// A completed local drain is not repeated by subsequent persistence retries within an owned save.
/// Handler failures can repeat handler entry; there are no per-handler checkpoints. Local handlers must
/// remain replay-safe and avoid rollback-unsafe external effects. Once a participant prevents retry
/// (such as a job write enlisted in the unit of work), any failure propagates and requires a fresh context and
/// graph. Caller-owned successful saves clear only their saved batches before physical commit; a known outer
/// rollback requires a fresh context and graph. Outbox storage can enlist atomically; delivery and external
/// effects remain at-least-once.
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
        CancellationToken cancellationToken = default
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
/// Unit of work (KD13/R11): the unit bound to the context by <c>IUnitOfWorkManager.BeginAsync(db)</c> /
/// <c>Enlist(db, tx)</c> is consulted first and the scope's manager second. A bound unit owned by another
/// scope's manager (a context created through <c>IDbContextFactory&lt;T&gt;</c> owns its own scope) is adopted
/// into this scope for the save's duration, so domain-event handlers and the outbox dispatcher resolved here
/// see the same <c>Current</c>. A save inside a caller-owned transaction that carries integration events
/// requires a unit that owns that transaction; otherwise it fails before any dispatch rather than writing
/// outbox rows non-atomically.
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
internal sealed class HeadlessSaveChangesPipeline(
    IServiceProvider serviceProvider,
    HeadlessDbContextOptions options,
    IHeadlessAuditPersistence auditPersistence,
    IUnitOfWorkManager unitOfWorkManager,
    IDomainEventDispatcher? domainEventDispatcher = null,
    IHeadlessOutboxDispatcher? outboxDispatcher = null,
    ILogger<HeadlessSaveChangesPipeline>? logger = null
) : IHeadlessSaveChangesPipeline
{
    private const string _MissingDomainEventDispatcherMessage =
        "Headless EF collected domain events to publish, but no IDomainEventDispatcher is registered. "
        + "Call AddHeadlessDbContextServices(...).AddDomainEvents() (or services.AddHeadlessDomainEventDispatcher()).";

    private const string _MissingOutboxDispatcherMessage =
        "Headless EF collected integration events to enqueue, but no IHeadlessOutboxDispatcher is registered. "
        + "Reference the Headless.EntityFramework.Messaging package and call "
        + "AddHeadlessDbContextServices(...).AddIntegrationEventOutbox().";

    private readonly IReadOnlyList<IHeadlessSaveEntryProcessor> _entryProcessors = options.ResolveSaveEntryProcessors(
        serviceProvider
    );

    // Looked up on every domain-event recollection pass of every save; the chain is fixed for the pipeline's
    // lifetime, so it is resolved once from the same instances the chain runs and cached (a null result is
    // cached too, hence the separate flag).
    private HeadlessMessageCollectorSaveEntryProcessor? _messageCollector;
    private bool _messageCollectorResolved;

    private readonly ILogger<HeadlessSaveChangesPipeline> _logger =
        logger ?? NullLogger<HeadlessSaveChangesPipeline>.Instance;

    public async Task<int> SaveChangesAsync(
        DbContext context,
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        // A unit bound to this context by another scope's manager becomes this scope's Current for the whole
        // save, so everything resolved here (handlers, the outbox dispatcher) coordinates on it. Re-entrant
        // when the slot already holds it; a different active unit in the slot is a programming error.
        using var adoption = _AdoptBoundUnitOfWork(context);

        // Materialize once — the framework processors don't add new ChangeTracker entries during
        // _ProcessEntries, so a single snapshot is correct for the audit capture too.
        var trackedEntries = _SnapshotEntries(context);
        var saveContext = _ProcessEntries(context, trackedEntries);
        var auditEntries = auditPersistence.CaptureEntries(trackedEntries);

        var state = new AsyncSaveState(
            context,
            saveContext,
            new StrongBox<IReadOnlyList<AuditLogEntryData>?>(auditEntries),
            acceptAllChangesOnSuccess,
            baseSaveChangesAsync,
            new StrongBox<bool>(),
            cancellationToken
        );

        if (context.Database.CurrentTransaction is not null)
        {
            _EnsureUnitOfWorkOwnsCallerTransaction(context, saveContext);

            return await _ExecuteWithinCurrentTransactionAsync(state).ConfigureAwait(false);
        }

        if (!_RequiresExplicitTransaction(auditEntries, saveContext))
        {
            var result = await baseSaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken).ConfigureAwait(false);
            _CompleteSuccessfulSave(context, saveContext, default, acceptAllChangesOnSuccess);

            return result;
        }

        var saved = await context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(state, _ExecuteWithNewTransactionAsync)
            .ConfigureAwait(false);
        saveContext.NonRetryableFailure?.Throw();
        return saved;
    }

    public int SaveChanges(DbContext context, Func<bool, int> baseSaveChanges, bool acceptAllChangesOnSuccess)
    {
#pragma warning disable MA0045 // Sync SaveChanges intentionally wraps EF sync APIs.
        using var adoption = _AdoptBoundUnitOfWork(context);

        var trackedEntries = _SnapshotEntries(context);
        var saveContext = _ProcessEntries(context, trackedEntries);
        var auditEntries = auditPersistence.CaptureEntries(trackedEntries);

        var state = new SaveState(
            context,
            saveContext,
            new StrongBox<IReadOnlyList<AuditLogEntryData>?>(auditEntries),
            acceptAllChangesOnSuccess,
            baseSaveChanges,
            new StrongBox<bool>()
        );

        if (context.Database.CurrentTransaction is not null)
        {
            _EnsureUnitOfWorkOwnsCallerTransaction(context, saveContext);

            return _ExecuteWithinCurrentTransaction(state);
        }

        if (!_RequiresExplicitTransaction(auditEntries, saveContext))
        {
            var result = baseSaveChanges(acceptAllChangesOnSuccess);
            _CompleteSuccessfulSave(context, saveContext, default, acceptAllChangesOnSuccess);

            return result;
        }

        var saved = context.Database.CreateExecutionStrategy().Execute(state, _ExecuteWithNewTransaction);
        saveContext.NonRetryableFailure?.Throw();
        return saved;
#pragma warning restore MA0045
    }

    private HeadlessMessageCollectorSaveEntryProcessor? _ResolveMessageCollector()
    {
        if (!_messageCollectorResolved)
        {
            _messageCollector = _entryProcessors.OfType<HeadlessMessageCollectorSaveEntryProcessor>().SingleOrDefault();
            _messageCollectorResolved = true;
        }

        return _messageCollector;
    }

    private static EntityEntry[] _SnapshotEntries(DbContext context)
    {
        // Single allocation, single ChangeTracker traversal — feeds both _ProcessEntries and the
        // initial audit capture.
        return [.. context.ChangeTracker.Entries()];
    }

    private IDisposable? _AdoptBoundUnitOfWork(DbContext context)
    {
        var bound = DbContextUnitOfWork.Find(context);

        return bound is null ? null : unitOfWorkManager.Adopt(bound);
    }

    // Integration events are the writes that must land inside the caller's transaction (outbox rows); a save
    // without them under a caller-owned transaction is ordinary EF usage and needs no unit of work. Handlers can
    // still add integration events during the drain — the outbox dispatcher repeats this check at dispatch time.
    private void _EnsureUnitOfWorkOwnsCallerTransaction(DbContext context, HeadlessSaveEntryContext saveContext)
    {
        if (saveContext.IntegrationEventEmitters.Count == 0)
        {
            return;
        }

        // CurrentTransaction was verified non-null by the caller; null-forgiving here documents that.
        var currentTransaction = context.Database.CurrentTransaction!.GetDbTransaction();

        if (
            unitOfWorkManager.Current?.Resource is IRelationalUnitOfWorkResource resource
            && ReferenceEquals(resource.Transaction, currentTransaction)
        )
        {
            return;
        }

        throw new InvalidOperationException(HeadlessUnitOfWorkMessages.CallerOwnedTransactionWithoutUnitOfWork);
    }

    private HeadlessSaveEntryContext _ProcessEntries(DbContext context, IReadOnlyList<EntityEntry> entries)
    {
        var tenantId = context is IHeadlessDbContext headlessContext ? headlessContext.TenantId : null;
        var saveContext = new HeadlessSaveEntryContext(context, tenantId);

        foreach (var entry in entries)
        {
            saveContext.ProcessedEntities.Add(entry.Entity);
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
        var retryPrevented = false;

        try
        {
            await using var transaction = await state
                .Context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, state.CancellationToken)
                .ConfigureAwait(false);

            // Observed mode: the pipeline commits, the unit only makes the transaction visible to everything
            // invoked inside the save (outbox writer, job writer, handlers) through the scope's Current and
            // drains their after-commit registrations once the commit is durable.
            await using var unitOfWork = unitOfWorkManager.Enlist(state.Context, transaction);
            int saved;

            try
            {
                saved = await _SaveWithinTransactionAsync(state, transaction, commitTransaction: true)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (!state.SaveContext.CommitStarted)
            {
                // The transaction rolls back when it is disposed below; tell the unit so participants observe a
                // rollback (not an abandon), and read the retry marker before the unit reaches its terminal state.
                retryPrevented = unitOfWork.IsRetryPrevented;
                await unitOfWork.RollbackAsync().ConfigureAwait(false);

                throw;
            }

            await unitOfWork.CompleteAsync(state.CancellationToken).ConfigureAwait(false);

            return saved;
        }
        catch (Exception exception) when (state.SaveContext.CommitStarted || retryPrevented)
        {
            // Commit outcomes may be unknown, and writes enlisted in the unit are absent from the retained tracker.
            // Rethrow outside the execution strategy so it cannot replay an incomplete unit of work.
            state.SaveContext.NonRetryableFailure = ExceptionDispatchInfo.Capture(exception);
            return 0;
        }
    }

    private int _ExecuteWithNewTransaction(SaveState state)
    {
        var retryPrevented = false;

        try
        {
#pragma warning disable MA0045, AsyncFixer04 // Sync intentionally; _RunBlocking blocks on each unit-of-work verb before the using block ends.
            // Sync twin of _ExecuteWithNewTransactionAsync — same open-then-enlist-then-complete shape.
            using var transaction = state.Context.Database.BeginTransaction(IsolationLevel.ReadCommitted);
            using var unitOfWork = unitOfWorkManager.Enlist(state.Context, transaction);
            int saved;

            try
            {
                saved = _SaveWithinTransaction(state, transaction, commitTransaction: true);
            }
            catch (Exception) when (!state.SaveContext.CommitStarted)
            {
                retryPrevented = unitOfWork.IsRetryPrevented;
                _RunBlocking(unitOfWork.RollbackAsync());

                throw;
            }

            _RunBlocking(unitOfWork.CompleteAsync(CancellationToken.None));

            return saved;
#pragma warning restore MA0045, AsyncFixer04
        }
        catch (Exception exception) when (state.SaveContext.CommitStarted || retryPrevented)
        {
            // Commit outcomes may be unknown, and writes enlisted in the unit are absent from the retained tracker.
            // Rethrow outside the execution strategy so it cannot replay an incomplete unit of work.
            state.SaveContext.NonRetryableFailure = ExceptionDispatchInfo.Capture(exception);
            return 0;
        }
    }

    // Intentional sync/async twin of _SaveWithinTransaction below: identical save policy (completed-drain
    // domain-event loop, integration flatten+dispatch, audit capture, missing-dispatcher guards). The two
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
            // A completed drain is retained across persistence retries. Handler failure is not a per-handler checkpoint.
            if (!state.DomainEventsPublished.Value)
            {
                while (_TryTakeDomainOccurrence(state.Context, state.SaveContext) is { } occurrence)
                {
                    state.CancellationToken.ThrowIfCancellationRequested();
                    var dispatcher =
                        domainEventDispatcher
                        ?? throw new InvalidOperationException(_MissingDomainEventDispatcherMessage);
                    await dispatcher.DispatchAsync(occurrence, state.CancellationToken).ConfigureAwait(false);
                    state.SaveContext.DomainEventCursor++;
                }

                state.AuditEntries.Value = auditPersistence.CaptureEntries(_SnapshotEntries(state.Context));
                state.DomainEventsPublished.Value = true;
            }

            var deferAcceptAllChanges = commitTransaction || _HasAuditEntries(state.AuditEntries.Value);
            var result = await state
                .BaseSaveChangesAsync(
                    !deferAcceptAllChanges && state.AcceptAllChangesOnSuccess,
                    state.CancellationToken
                )
                .ConfigureAwait(false);

            auditSave = await auditPersistence
                .ResolveAndPersistAsync(
                    state.Context,
                    state.AuditEntries.Value,
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
                    .DistinctBy(static occurrence => occurrence.EventId, StringComparer.Ordinal)
                    .ToArray();

                await dispatcher.DispatchAsync(integrationEvents, state.CancellationToken).ConfigureAwait(false);
            }

            if (commitTransaction)
            {
                state.SaveContext.CommitStarted = true;
                await transaction.CommitAsync(state.CancellationToken).ConfigureAwait(false);
                if (state.AcceptAllChangesOnSuccess)
                {
                    state.Context.ChangeTracker.AcceptAllChanges();
                }
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

    // Intentional sync/async twin of _SaveWithinTransactionAsync above: identical save policy (completed-drain
    // domain-event loop, integration flatten+dispatch, audit capture, missing-dispatcher guards). The two
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
            if (!state.DomainEventsPublished.Value)
            {
                while (_TryTakeDomainOccurrence(state.Context, state.SaveContext) is { } occurrence)
                {
                    var dispatcher =
                        domainEventDispatcher
                        ?? throw new InvalidOperationException(_MissingDomainEventDispatcherMessage);
                    _DispatchDomainEventBlocking(dispatcher, occurrence);
                    state.SaveContext.DomainEventCursor++;
                }

                state.AuditEntries.Value = auditPersistence.CaptureEntries(_SnapshotEntries(state.Context));
                state.DomainEventsPublished.Value = true;
            }

            var deferAcceptAllChanges = commitTransaction || _HasAuditEntries(state.AuditEntries.Value);
            var result = state.BaseSaveChanges(!deferAcceptAllChanges && state.AcceptAllChangesOnSuccess);
            auditSave = auditPersistence.ResolveAndPersist(
                state.Context,
                state.AuditEntries.Value,
                state.BaseSaveChanges
            );

            if (state.SaveContext.IntegrationEventEmitters.Count > 0)
            {
                var dispatcher =
                    outboxDispatcher ?? throw new InvalidOperationException(_MissingOutboxDispatcherMessage);

                var integrationEvents = state
                    .SaveContext.IntegrationEventEmitters.SelectMany(static emitter => emitter.Events)
                    .DistinctBy(static occurrence => occurrence.EventId, StringComparer.Ordinal)
                    .ToArray();

                dispatcher.Dispatch(integrationEvents);
            }

            if (commitTransaction)
            {
                state.SaveContext.CommitStarted = true;
                transaction.Commit();
                if (state.AcceptAllChangesOnSuccess)
                {
                    state.Context.ChangeTracker.AcceptAllChanges();
                }
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

    // The finite budget also covers lifecycle events and new emitters, so recursive handlers fail before saving.
    private const int _MaximumDomainOccurrencesPerSave = 1024;

    private EventContext<object>? _TryTakeDomainOccurrence(DbContext context, HeadlessSaveEntryContext saveContext)
    {
        // Recollect after each completed pass, without synthesizing lifecycle events again for existing entries.
        if (saveContext.DomainEventCursor == saveContext.PendingDomainEvents.Count)
        {
            var collector = _ResolveMessageCollector();
            foreach (var entry in _SnapshotEntries(context))
            {
                if (saveContext.ProcessedEntities.Add(entry.Entity))
                {
                    foreach (var processor in _entryProcessors)
                    {
                        processor.Process(entry, saveContext);
                    }
                }
                else
                {
                    collector?.Process(entry, saveContext);
                }
            }
        }

        if (saveContext.DomainEventCursor == saveContext.PendingDomainEvents.Count)
        {
            return null;
        }

        if (saveContext.DomainEventCursor >= _MaximumDomainOccurrencesPerSave)
        {
            throw new InvalidOperationException(
                $"Domain event drain exceeded {_MaximumDomainOccurrencesPerSave} occurrences in one save; check for recursive emissions."
            );
        }

        return saveContext.PendingDomainEvents[saveContext.DomainEventCursor];
    }

    // IDomainEventDispatcher is async-only by contract: a public synchronous dispatch method would invite sync-over-async
    // dispatch (and its synchronization-context deadlocks) in application code. The synchronous
    // SaveChanges path still has to dispatch domain events inline, so the bridge lives HERE, contained
    // in infrastructure. Blocking is acceptable in this frame: EF's own sync SaveChanges is already
    // blocking database I/O on a thread without a synchronization context to deadlock against.
    private static void _DispatchDomainEventBlocking(
        IDomainEventDispatcher dispatcher,
        EventContext<object> domainEvent
    )
    {
#pragma warning disable MA0045 // Sync SaveChanges path intentionally blocks; see comment above.
        var pending = dispatcher.DispatchAsync(domainEvent);

        if (pending.IsCompletedSuccessfully)
        {
            // Observe the completed ValueTask (required for IValueTaskSource-backed implementations).
            pending.GetAwaiter().GetResult();
            return;
        }

        // GetResult() rethrows the original exception (no AggregateException wrapping by Task.Wait),
        // preserving the dispatcher's single-exception / AggregateException contract for the catch below.
        pending.AsTask().GetAwaiter().GetResult();
#pragma warning restore MA0045
    }

    // The unit-of-work verbs are async-only by contract; the synchronous SaveChanges path blocks on them here,
    // in infrastructure, for the same reason as the domain-event bridge above.
    private static void _RunBlocking(ValueTask pending)
    {
#pragma warning disable MA0045 // Sync SaveChanges path intentionally blocks; see comment above.
        if (pending.IsCompletedSuccessfully)
        {
            pending.GetAwaiter().GetResult();
            return;
        }

        pending.AsTask().GetAwaiter().GetResult();
#pragma warning restore MA0045
    }

    private static bool _RequiresExplicitTransaction(
        IReadOnlyList<AuditLogEntryData>? auditEntries,
        HeadlessSaveEntryContext saveContext
    )
    {
        return _HasAuditEntries(auditEntries)
            || saveContext.IntegrationEventEmitters.Count > 0
            || saveContext.DomainEventEmitters.Count > 0;
    }

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
        StrongBox<IReadOnlyList<AuditLogEntryData>?> AuditEntries,
        bool AcceptAllChangesOnSuccess,
        Func<bool, CancellationToken, Task<int>> BaseSaveChangesAsync,
        // Shared across the by-value state copies the execution strategy makes on retry, so the
        // completed-drain guard in _SaveWithinTransactionAsync survives a replay. See the publish loop there.
        StrongBox<bool> DomainEventsPublished,
        CancellationToken CancellationToken
    );

    private readonly record struct SaveState(
        DbContext Context,
        HeadlessSaveEntryContext SaveContext,
        StrongBox<IReadOnlyList<AuditLogEntryData>?> AuditEntries,
        bool AcceptAllChangesOnSuccess,
        Func<bool, int> BaseSaveChanges,
        // Shared across the by-value state copies the execution strategy makes on retry, so the
        // completed-drain guard in _SaveWithinTransaction survives a replay. See the publish loop there.
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
