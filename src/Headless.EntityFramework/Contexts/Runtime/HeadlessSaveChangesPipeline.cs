// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Headless.AuditLog;
using Headless.Domain;
using Headless.EntityFramework.Contexts;
using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Headless.EntityFramework;

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
/// Unit of work: the unit bound to the context by <c>IUnitOfWorkFactory.BeginAsync(db)</c> /
/// <c>Enlist(db, tx)</c> / <c>RunAsync(db, …)</c> is the one a caller-owned save enlists in; a pipeline-owned
/// save enlists its own transaction and binds that unit to the context for the save's duration. A context built
/// over the connection of a sibling context that carries a unit adopts that unit before the save, so its save is
/// a caller-owned save inside the sibling's transaction. The unit
/// travels explicitly: the outbox dispatcher receives it as an argument, and a domain-event handler reads it
/// from the context it was handed (<c>db.UnitOfWork()</c>). A save inside a caller-owned transaction that
/// carries integration events requires a unit that owns that transaction; otherwise it fails before any
/// dispatch rather than writing outbox rows non-atomically.
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
    IUnitOfWorkFactory unitOfWorkFactory,
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

    // An array, so the per-entry processor loop enumerates without allocating.
    private readonly IHeadlessSaveEntryProcessor[] _entryProcessors = options.ResolveSaveEntryProcessors(
        serviceProvider
    );

    private readonly bool _hasOnlyBuiltInProcessors = options.HasOnlyBuiltInSaveEntryProcessors;

    // Only the built-in dispatcher resolves handlers from DI, so only it can say a payload has none; any other
    // dispatcher may deliver events elsewhere, and every event it would receive is still synthesized and dispatched.
    private readonly Func<Type, bool>? _domainEventHandlerFilter = domainEventDispatcher
        is ServiceProviderDomainEventDispatcher builtIn
        ? builtIn.HasHandler
        : null;

    private readonly ILogger<HeadlessSaveChangesPipeline> _logger =
        logger ?? NullLogger<HeadlessSaveChangesPipeline>.Instance;

    public async Task<int> SaveChangesAsync(
        DbContext context,
        Func<bool, CancellationToken, Task<int>> baseSaveChangesAsync,
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        // Materialize once — the framework processors don't add new ChangeTracker entries during
        // _ProcessEntries, so a single snapshot is correct for the audit capture too.
        var trackedEntries = _SnapshotEntries(context);
        var saveContext = _ProcessEntries(context, trackedEntries);
        var auditEntries = auditPersistence.CaptureEntries(trackedEntries, saveContext);

        var state = new AsyncSaveState(
            context,
            saveContext,
            new StrongBox<IReadOnlyList<AuditLogEntryData>?>(auditEntries),
            acceptAllChangesOnSuccess,
            baseSaveChangesAsync,
            new StrongBox<bool>(),
            cancellationToken
        );

        // Adopts the unit a sibling context began over this context's connection, so the save runs inside that
        // unit's transaction instead of beginning a second one on a connection that already has one.
        _ = context.UnitOfWork();

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
        var trackedEntries = _SnapshotEntries(context);
        var saveContext = _ProcessEntries(context, trackedEntries);
        var auditEntries = auditPersistence.CaptureEntries(trackedEntries, saveContext);

        var state = new SaveState(
            context,
            saveContext,
            new StrongBox<IReadOnlyList<AuditLogEntryData>?>(auditEntries),
            acceptAllChangesOnSuccess,
            baseSaveChanges,
            new StrongBox<bool>()
        );

        // Same adoption as the async path.
        _ = context.UnitOfWork();

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
    }

    // Resolved per recollection pass rather than cached: the pipeline instance is shared by every context in the
    // scope, so a lazily written cache would need synchronization, and the chain is a handful of processors.
    private HeadlessMessageCollectorSaveEntryProcessor? _ResolveMessageCollector()
    {
        return _entryProcessors.OfType<HeadlessMessageCollectorSaveEntryProcessor>().SingleOrDefault();
    }

    private static EntityEntry[] _SnapshotEntries(DbContext context)
    {
        // Single allocation, single ChangeTracker traversal — feeds both _ProcessEntries and the
        // initial audit capture.
        return [.. context.ChangeTracker.Entries()];
    }

    // The save's own exception is the caller's outcome; a fault while rolling the unit back (its failure drain
    // disposing scope-local state) is logged so it can never replace the failure that caused the rollback.
    private async ValueTask _RollBackQuietlyAsync(IUnitOfWork unitOfWork)
    {
        try
        {
            await unitOfWork.RollbackAsync().ConfigureAwait(false);
        }
        catch (Exception rollbackFault)
        {
            _logger.LogUnitOfWorkRollbackFailed(rollbackFault);
        }
    }

    // Integration events are the writes that must land inside the caller's transaction (outbox rows); a save
    // without them under a caller-owned transaction is ordinary EF usage and needs no unit of work. Handlers can
    // still add integration events during the drain — the outbox dispatcher repeats this check at dispatch time.
    private static void _EnsureUnitOfWorkOwnsCallerTransaction(DbContext context, HeadlessSaveEntryContext saveContext)
    {
        if (saveContext.IntegrationEventEmitters.Count == 0)
        {
            return;
        }

        // CurrentTransaction was verified non-null by the caller; null-forgiving here documents that.
        var currentTransaction = context.Database.CurrentTransaction!.GetDbTransaction();

        if (
            context.UnitOfWork()?.Resource is IRelationalUnitOfWorkResource resource
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
        var saveContext = new HeadlessSaveEntryContext(context, tenantId)
        {
            TrackedEntries = entries,
            DomainEventHandlerFilter = _domainEventHandlerFilter,
        };

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
        return _SaveWithinTransactionAsync(
            state,
            currentTransaction,
            state.Context.UnitOfWork(),
            commitTransaction: false
        );
    }

    private int _ExecuteWithinCurrentTransaction(SaveState state)
    {
        // CurrentTransaction was just verified non-null above; null-forgiving here documents that.
        var currentTransaction = state.Context.Database.CurrentTransaction!;
        return _SaveWithinTransaction(state, currentTransaction, state.Context.UnitOfWork(), commitTransaction: false);
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
            // invoked inside the save — the outbox dispatcher receives it, handlers read it from the context —
            // and drains their after-commit registrations once the commit is durable.
            await using var unitOfWork = unitOfWorkFactory.Enlist(state.Context, transaction);
            int saved;

            try
            {
                saved = await _SaveWithinTransactionAsync(state, transaction, unitOfWork, commitTransaction: true)
                    .ConfigureAwait(false);
            }
            catch (Exception) when (!state.SaveContext.CommitStarted)
            {
                // The transaction rolls back when it is disposed below; tell the unit so participants observe a
                // rollback (not an abandon), and read the retry marker before the unit reaches its terminal state.
                retryPrevented = unitOfWork.IsRetryPrevented;
                await _RollBackQuietlyAsync(unitOfWork).ConfigureAwait(false);

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
#pragma warning disable AsyncFixer04 // Sync intentionally; _RunBlocking blocks on each unit-of-work verb before the using block ends.
            // Sync twin of _ExecuteWithNewTransactionAsync — same open-then-enlist-then-complete shape.
            using var transaction = state.Context.Database.BeginTransaction(IsolationLevel.ReadCommitted);
            using var unitOfWork = unitOfWorkFactory.Enlist(state.Context, transaction);
            int saved;

            try
            {
                saved = _SaveWithinTransaction(state, transaction, unitOfWork, commitTransaction: true);
            }
            catch (Exception) when (!state.SaveContext.CommitStarted)
            {
                retryPrevented = unitOfWork.IsRetryPrevented;
                _RunBlocking(_RollBackQuietlyAsync(unitOfWork));

                throw;
            }

            _RunBlocking(unitOfWork.CompleteAsync(CancellationToken.None));

            return saved;
#pragma warning restore AsyncFixer04
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
        IUnitOfWork? unitOfWork,
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
                    if (_MarkHandledOccurrence(state.SaveContext, occurrence))
                    {
                        await dispatcher.DispatchAsync(occurrence, state.CancellationToken).ConfigureAwait(false);
                    }

                    state.SaveContext.DomainEventCursor++;
                }

                _RecaptureAuditEntries(state.Context, state.SaveContext, state.AuditEntries);
                state.DomainEventsPublished.Value = true;
            }

            // Audit rows that are complete as captured join the entity save as one batch; the rest wait for
            // store-generated keys. Assigned before the save so a failure discards the rows already added.
            var split = await auditPersistence
                .AddReadyEntriesAsync(state.Context, state.AuditEntries.Value, state.CancellationToken)
                .ConfigureAwait(false);
            auditSave = split.Added;

            var deferAcceptAllChanges = commitTransaction || _HasAuditEntries(state.AuditEntries.Value);
            var result = await state
                .BaseSaveChangesAsync(
                    !deferAcceptAllChanges && state.AcceptAllChangesOnSuccess,
                    state.CancellationToken
                )
                .ConfigureAwait(false);

            // The audit rows are not the caller's changes, so they stay out of the returned count.
            result = Math.Max(0, result - split.Added.Count);

            auditSave = auditSave.Merge(
                await auditPersistence
                    .ResolveAndPersistAsync(
                        state.Context,
                        split.Deferred,
                        state.BaseSaveChangesAsync,
                        state.CancellationToken
                    )
                    .ConfigureAwait(false)
            );

            if (state.SaveContext.IntegrationEventEmitters.Count > 0)
            {
                var dispatcher =
                    outboxDispatcher ?? throw new InvalidOperationException(_MissingOutboxDispatcherMessage);

                var integrationEvents = state
                    .SaveContext.IntegrationEventEmitters.SelectMany(static emitter => emitter.Events)
                    .DistinctBy(static occurrence => occurrence.EventId, StringComparer.Ordinal)
                    .ToArray();

                // Handlers can add integration events during the drain, so a caller-owned save that started with
                // none is re-checked here: with no unit owning the transaction the events would ship non-atomically.
                var owner =
                    unitOfWork
                    ?? throw new InvalidOperationException(
                        HeadlessUnitOfWorkMessages.CallerOwnedTransactionWithoutUnitOfWork
                    );

                await dispatcher.DispatchAsync(owner, integrationEvents, state.CancellationToken).ConfigureAwait(false);
            }

            if (commitTransaction)
            {
                state.SaveContext.CommitStarted = true;
                await transaction.CommitAsync(state.CancellationToken).ConfigureAwait(false);
                if (state.AcceptAllChangesOnSuccess)
                {
                    // Detach the audit rows first: accepting them would snapshot their JSON columns for a row that
                    // is about to leave the tracker.
                    auditPersistence.ReleaseEntries(auditSave);
                    state.Context.ChangeTracker.AcceptAllChanges();
                }
            }
            else if (_DispatchedOccurrences(state.SaveContext))
            {
                // A caller-owned save is replayed by the caller's block, not by this pipeline, and the clear below
                // leaves that replay nothing to dispatch: the handlers' and the integration bridge's enlisted rows
                // roll back with the attempt while the emitters stay empty, so the replayed block would commit the
                // aggregate without its messages. End replay while the rows are still atomic with the save.
                unitOfWork?.PreventRetry();
            }
            else if (_WroteIntoObservedUnitThroughSibling(state.Context, unitOfWork, result))
            {
                // This context joined a unit another context's pipeline save enlisted, most likely from one of that
                // save's domain-event handlers. The owning pipeline replays its own save without re-running those
                // handlers, so the rows written here would roll back with the attempt and never be restored.
                unitOfWork!.PreventRetry();
            }

            _CompleteSuccessfulSave(
                state.Context,
                state.SaveContext,
                auditSave,
                // The commit branch above already accepted; accepting again would walk the tracker twice.
                state.AcceptAllChangesOnSuccess && !commitTransaction
            );

            return result;
        }
        catch (Exception caught)
        {
            try
            {
                auditPersistence.DiscardEntries(auditSave);
            }
            catch (Exception discardFailure)
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
    private int _SaveWithinTransaction(
        SaveState state,
        IDbContextTransaction transaction,
        IUnitOfWork? unitOfWork,
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
            if (!state.DomainEventsPublished.Value)
            {
                while (_TryTakeDomainOccurrence(state.Context, state.SaveContext) is { } occurrence)
                {
                    var dispatcher =
                        domainEventDispatcher
                        ?? throw new InvalidOperationException(_MissingDomainEventDispatcherMessage);
                    if (_MarkHandledOccurrence(state.SaveContext, occurrence))
                    {
                        _DispatchDomainEventBlocking(dispatcher, occurrence);
                    }

                    state.SaveContext.DomainEventCursor++;
                }

                _RecaptureAuditEntries(state.Context, state.SaveContext, state.AuditEntries);
                state.DomainEventsPublished.Value = true;
            }

            // Same split as the async twin: complete audit rows join the entity save, the rest wait for generated keys.
            var split = auditPersistence.AddReadyEntries(state.Context, state.AuditEntries.Value);
            auditSave = split.Added;

            var deferAcceptAllChanges = commitTransaction || _HasAuditEntries(state.AuditEntries.Value);
            var result = state.BaseSaveChanges(!deferAcceptAllChanges && state.AcceptAllChangesOnSuccess);
            result = Math.Max(0, result - split.Added.Count);

            auditSave = auditSave.Merge(
                auditPersistence.ResolveAndPersist(state.Context, split.Deferred, state.BaseSaveChanges)
            );

            if (state.SaveContext.IntegrationEventEmitters.Count > 0)
            {
                var dispatcher =
                    outboxDispatcher ?? throw new InvalidOperationException(_MissingOutboxDispatcherMessage);

                var integrationEvents = state
                    .SaveContext.IntegrationEventEmitters.SelectMany(static emitter => emitter.Events)
                    .DistinctBy(static occurrence => occurrence.EventId, StringComparer.Ordinal)
                    .ToArray();

                var owner =
                    unitOfWork
                    ?? throw new InvalidOperationException(
                        HeadlessUnitOfWorkMessages.CallerOwnedTransactionWithoutUnitOfWork
                    );

                dispatcher.Dispatch(owner, integrationEvents);
            }

            if (commitTransaction)
            {
                state.SaveContext.CommitStarted = true;
                transaction.Commit();
                if (state.AcceptAllChangesOnSuccess)
                {
                    // Detach the audit rows first: accepting them would snapshot their JSON columns for a row that
                    // is about to leave the tracker.
                    auditPersistence.ReleaseEntries(auditSave);
                    state.Context.ChangeTracker.AcceptAllChanges();
                }
            }
            else if (_DispatchedOccurrences(state.SaveContext))
            {
                // Same rule as the async twin: the caller's block replays this save, and the clear below leaves
                // it nothing to dispatch, so end replay while the enlisted rows are still atomic with the save.
                unitOfWork?.PreventRetry();
            }
            else if (_WroteIntoObservedUnitThroughSibling(state.Context, unitOfWork, result))
            {
                // Same rule as the async twin: the owning pipeline replays without re-running the handler that
                // saved through this sibling, so its rows would be lost with the rolled-back attempt.
                unitOfWork!.PreventRetry();
            }

            _CompleteSuccessfulSave(
                state.Context,
                state.SaveContext,
                auditSave,
                // The commit branch above already accepted; accepting again would walk the tracker twice.
                state.AcceptAllChangesOnSuccess && !commitTransaction
            );

            return result;
        }
        catch (Exception caught)
        {
            try
            {
                auditPersistence.DiscardEntries(auditSave);
            }
            catch (Exception discardFailure)
            {
                _logger.LogAuditDiscardFailed(discardFailure);
            }

            ExceptionDispatchInfo.Capture(caught).Throw();
            throw; // unreachable; satisfies analyzers
        }
    }

    /// <summary>
    /// Records that a handler is about to run for <paramref name="occurrence"/>, or reports that none is registered:
    /// an occurrence nobody handles is skipped, and it cannot change the graph.
    /// </summary>
    private static bool _MarkHandledOccurrence(HeadlessSaveEntryContext saveContext, EventContext<object> occurrence)
    {
        if (!saveContext.HasDomainEventHandler(occurrence.Payload.GetType()))
        {
            return false;
        }

        saveContext.HandlerMayHaveChangedGraph = true;
        saveContext.GraphChangedSinceSnapshot = true;

        return true;
    }

    /// <summary>
    /// Refreshes the audit entries captured before the drain when the drain could have changed what they describe.
    /// </summary>
    /// <remarks>
    /// With only the built-in processors, every framework write goes through the change tracker, so the entries
    /// captured before the drain are still exact unless a handler ran; after one ran, the drain's last recollection
    /// already took the snapshot to capture from. A consumer processor may write CLR members that only change
    /// detection reveals, so a chain with one keeps taking a fresh snapshot.
    /// </remarks>
    private void _RecaptureAuditEntries(
        DbContext context,
        HeadlessSaveEntryContext saveContext,
        StrongBox<IReadOnlyList<AuditLogEntryData>?> auditEntries
    )
    {
        if (!_hasOnlyBuiltInProcessors)
        {
            auditEntries.Value = auditPersistence.CaptureEntries(_SnapshotEntries(context), saveContext);

            return;
        }

        if (saveContext.HandlerMayHaveChangedGraph)
        {
            auditEntries.Value = auditPersistence.CaptureEntries(
                saveContext.TrackedEntries ?? _SnapshotEntries(context),
                saveContext
            );
        }
    }

    private static HashSet<object> _CollectEntities(IReadOnlyList<EntityEntry>? entries)
    {
        var entities = new HashSet<object>(entries?.Count ?? 0, ReferenceEqualityComparer.Instance);

        if (entries is not null)
        {
            foreach (var entry in entries)
            {
                entities.Add(entry.Entity);
            }
        }

        return entities;
    }

    // The finite budget also covers lifecycle events and new emitters, so recursive handlers fail before saving.
    private const int _MaximumDomainOccurrencesPerSave = 1024;

    private EventContext<object>? _TryTakeDomainOccurrence(DbContext context, HeadlessSaveEntryContext saveContext)
    {
        // Recollect after each completed pass, without synthesizing lifecycle events again for existing entries. Only a
        // handler can add entities or events, so the last snapshot stays current until one has run: a save with no
        // occurrences, or whose occurrences have no handlers, skips a full tracker traversal.
        if (
            saveContext.DomainEventCursor == saveContext.PendingDomainEvents.Count
            && saveContext.GraphChangedSinceSnapshot
        )
        {
            var collector = _ResolveMessageCollector();
            saveContext.GraphChangedSinceSnapshot = false;
            // Every entry of the snapshot being replaced has been processed, by the first pass or a recollection.
            var processed = saveContext.ProcessedEntities ??= _CollectEntities(saveContext.TrackedEntries);
            var snapshot = _SnapshotEntries(context);
            saveContext.TrackedEntries = snapshot;
            foreach (var entry in snapshot)
            {
                if (processed.Add(entry.Entity))
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
    }

    // The unit-of-work verbs are async-only by contract; the synchronous SaveChanges path blocks on them here,
    // in infrastructure, for the same reason as the domain-event bridge above.
    private static void _RunBlocking(ValueTask pending)
    {
        if (pending.IsCompletedSuccessfully)
        {
            pending.GetAwaiter().GetResult();
            return;
        }

        pending.AsTask().GetAwaiter().GetResult();
    }

    private static bool _RequiresExplicitTransaction(
        IReadOnlyList<AuditLogEntryData>? auditEntries,
        HeadlessSaveEntryContext saveContext
    )
    {
        return _HasAuditEntries(auditEntries)
            || saveContext.IntegrationEventEmitters.Count > 0
            || _HasHandledDomainEvent(saveContext);
    }

    // A domain event without a handler has nothing to run inside the transaction; its emitter is cleared after the
    // save either way.
    private static bool _HasHandledDomainEvent(HeadlessSaveEntryContext saveContext)
    {
        var pending = saveContext.PendingDomainEvents;

        for (var i = 0; i < pending.Count; i++)
        {
            if (saveContext.HasDomainEventHandler(pending[i].Payload.GetType()))
            {
                return true;
            }
        }

        return false;
    }

    private static bool _HasAuditEntries(IReadOnlyList<AuditLogEntryData>? auditEntries)
    {
        return auditEntries is { Count: > 0 };
    }

    /// <summary>
    /// Whether this save handed at least one occurrence to a handler or to the outbox bridge. On the success path
    /// every pending domain occurrence has been dispatched, so the pending list stands in for the dispatched set.
    /// </summary>
    private static bool _DispatchedOccurrences(HeadlessSaveEntryContext saveContext)
    {
        return saveContext.PendingDomainEvents.Count > 0 || saveContext.IntegrationEventEmitters.Count > 0;
    }

    /// <summary>
    /// Whether a caller-owned save wrote rows into an observed unit that this context reached only through the
    /// connection it shares with the unit's context. Owned units stay replayable because their block re-runs the
    /// whole save, and the unit's own context stays replayable because its rows live in the tracker the owning
    /// pipeline replays.
    /// </summary>
    private static bool _WroteIntoObservedUnitThroughSibling(DbContext context, IUnitOfWork? unitOfWork, int written)
    {
        return written > 0
            && unitOfWork?.Resource is { IsOwned: false }
            && DbContextUnitOfWorkBinding.IsJoinedThroughConnection(context);
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

internal static partial class HeadlessSaveChangesPipelineLog
{
    [LoggerMessage(
        EventId = 1,
        EventName = "HeadlessAuditDiscardFailedDuringExceptionPath",
        Level = LogLevel.Error,
        Message = "Audit discard failed during exception path; rethrowing the original SaveChanges exception."
    )]
    public static partial void LogAuditDiscardFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2,
        EventName = "HeadlessUnitOfWorkRollbackFailedDuringExceptionPath",
        Level = LogLevel.Error,
        Message = "Rolling the save's unit of work back faulted during the exception path; rethrowing the original SaveChanges exception."
    )]
    public static partial void LogUnitOfWorkRollbackFailed(this ILogger logger, Exception exception);
}
