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
/// A completed local drain is not repeated by subsequent persistence retries within an owned save.
/// Handler failures can repeat handler entry; there are no per-handler checkpoints. Local handlers must
/// remain replay-safe and avoid rollback-unsafe external effects. Caller-owned successful saves clear only
/// their saved batches before physical commit; a known outer rollback requires a fresh context and graph. Outbox storage can enlist atomically; delivery and external effects remain at-least-once.
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
    IHeadlessTransactionCoordinator transactionCoordinator,
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
        + "Reference the Headless.EntityFramework.Messaging package and call "
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
        CancellationToken cancellationToken = default
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
            new StrongBox<IReadOnlyList<AuditLogEntryData>?>(auditEntries),
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

        var saved = await context
            .Database.CreateExecutionStrategy()
            .ExecuteAsync(state, _ExecuteWithNewTransactionAsync)
            .ConfigureAwait(false);
        saveContext.CommitFailure?.Throw();
        return saved;
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
            new StrongBox<IReadOnlyList<AuditLogEntryData>?>(auditEntries),
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

        var saved = context.Database.CreateExecutionStrategy().Execute(state, _ExecuteWithNewTransaction);
        saveContext.CommitFailure?.Throw();
        return saved;
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
        try
        {
            await using var transaction = await state
                .Context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, state.CancellationToken)
                .ConfigureAwait(false);
            // Give the selected transaction adapter the open transaction synchronously in this frame. The core adapter
            // is a no-op; the commit-coordination package pushes its ambient coordinator here so it flows to work
            // invoked inside the save. The push must not live behind an async helper because AsyncLocal state created
            // there does not propagate back to this caller.
            await using var coordination = transactionCoordinator.Enlist(
                state.Context.Database,
                transaction,
                serviceProvider,
                state.CancellationToken
            );
            return await _SaveWithinTransactionAsync(state, transaction, commitTransaction: true).ConfigureAwait(false);
        }
        catch (Exception exception) when (state.SaveContext.CommitStarted)
        {
            // A commit or post-commit notification failure does not prove rollback; do not replay the save.
            state.SaveContext.CommitFailure = ExceptionDispatchInfo.Capture(exception);
            return 0;
        }
    }

    private int _ExecuteWithNewTransaction(SaveState state)
    {
        try
        {
#pragma warning disable MA0045 // Sync intentionally
            // Sync twin of _ExecuteWithNewTransactionAsync — same open-then-synchronously-enlist shape.
            using var transaction = state.Context.Database.BeginTransaction(IsolationLevel.ReadCommitted);
            using var coordination = transactionCoordinator.Enlist(
                state.Context.Database,
                transaction,
                serviceProvider,
                CancellationToken.None
            );
            return _SaveWithinTransaction(state, transaction, commitTransaction: true);
#pragma warning restore MA0045
        }
        catch (Exception exception) when (state.SaveContext.CommitStarted)
        {
            // A commit or post-commit notification failure does not prove rollback; do not replay the save.
            state.SaveContext.CommitFailure = ExceptionDispatchInfo.Capture(exception);
            return 0;
        }
    }

    // Intentional sync/async twin of _SaveWithinTransaction below: identical save policy (completed-drain
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
            // A completed drain is retained across persistence retries. Handler failure is not a per-handler checkpoint.
            if (!state.DomainEventsPublished.Value)
            {
                while (_TryTakeDomainOccurrence(state.Context, state.SaveContext) is { } occurrence)
                {
                    state.CancellationToken.ThrowIfCancellationRequested();
                    var bus = localEventBus ?? throw new InvalidOperationException(_MissingLocalEventBusMessage);
                    await bus.PublishAsync(occurrence, state.CancellationToken).ConfigureAwait(false);
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
                    .DistinctBy(static occurrence => occurrence.Context.EventId, StringComparer.Ordinal)
                    .Select(static occurrence => occurrence.Payload)
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
            if (!state.DomainEventsPublished.Value)
            {
                while (_TryTakeDomainOccurrence(state.Context, state.SaveContext) is { } occurrence)
                {
                    var bus = localEventBus ?? throw new InvalidOperationException(_MissingLocalEventBusMessage);
                    _PublishDomainEventBlocking(bus, occurrence);
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
                    .DistinctBy(static occurrence => occurrence.Context.EventId, StringComparer.Ordinal)
                    .Select(static occurrence => occurrence.Payload)
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

    private EventOccurrence<IDomainEvent>? _TryTakeDomainOccurrence(
        DbContext context,
        HeadlessSaveEntryContext saveContext
    )
    {
        // Recollect after each completed pass, without synthesizing lifecycle events again for existing entries.
        if (saveContext.DomainEventCursor == saveContext.PendingDomainEvents.Count)
        {
            var collector = _entryProcessors.OfType<HeadlessMessageCollectorSaveEntryProcessor>().SingleOrDefault();
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

    // ILocalEventBus is async-only by contract: exposing a public sync Publish invited sync-over-async
    // dispatch (and its synchronization-context deadlocks) in application code. The synchronous
    // SaveChanges path still has to dispatch domain events inline, so the bridge lives HERE, contained
    // in infrastructure. Blocking is acceptable in this frame: EF's own sync SaveChanges is already
    // blocking database I/O on a thread without a synchronization context to deadlock against.
    private static void _PublishDomainEventBlocking(ILocalEventBus bus, EventOccurrence<IDomainEvent> domainEvent)
    {
#pragma warning disable MA0045 // Sync SaveChanges path intentionally blocks; see comment above.
        var pending = bus.PublishAsync(domainEvent);

        if (pending.IsCompletedSuccessfully)
        {
            // Observe the completed ValueTask (required for IValueTaskSource-backed implementations).
            pending.GetAwaiter().GetResult();
            return;
        }

        // GetResult() rethrows the original exception (no AggregateException wrapping by Task.Wait),
        // preserving the bus's single-exception / AggregateException contract for the catch below.
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
