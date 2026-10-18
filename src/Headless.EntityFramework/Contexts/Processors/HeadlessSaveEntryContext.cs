// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Headless.Context;
using Headless.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using AccountId = Headless.Primitives.AccountId;
using UserId = Headless.Primitives.UserId;

namespace Headless.EntityFramework;

/// <summary>
/// Per-<c>SaveChanges</c> scratchpad threaded through the processor chain. Holds the active
/// <see cref="DbContext"/> and the message-emitter snapshots collected this round.
/// </summary>
[PublicAPI]
public sealed class HeadlessSaveEntryContext(DbContext dbContext, string? tenantId = null)
{
    // Constructor parameters are positional — documented via the type summary and property docs.
    /// <summary>Active EF Core context for this save.</summary>
    public DbContext DbContext { get; } = dbContext;

    /// <summary>Ambient tenant identifier captured for this save round.</summary>
    public string? TenantId { get; } = tenantId;

    // The event buffers are created on first write: most saves carry no events, and every save builds a context.
    private List<EmitterDomainEvents>? _domainEventEmitters;
    private List<EmitterIntegrationEvents>? _integrationEventEmitters;
    private List<EventContext<object>>? _pendingDomainEvents;
    private HashSet<string>? _queuedDomainIds;
    private (UserId? UserId, AccountId? AccountId)? _actor;

    /// <summary>
    /// Domain-event emitters collected during processing. Internal plumbing between the terminal
    /// collector processor and the save pipeline; the pipeline publishes the events within the active
    /// transaction and then clears them via <see cref="ClearEmitterMessages"/>.
    /// </summary>
    internal IReadOnlyList<EmitterDomainEvents> DomainEventEmitters =>
        _domainEventEmitters ?? (IReadOnlyList<EmitterDomainEvents>)[];

    /// <summary>
    /// Integration-event emitters collected during processing. Internal plumbing consumed by the save
    /// pipeline to enqueue events into the outbox for post-commit delivery.
    /// </summary>
    internal IReadOnlyList<EmitterIntegrationEvents> IntegrationEventEmitters =>
        _integrationEventEmitters ?? (IReadOnlyList<EmitterIntegrationEvents>)[];

    internal bool CommitStarted { get; set; }

    /// <summary>
    /// The tracker snapshot the pipeline is processing right now. Processors that must look up other tracked entries
    /// read it instead of enumerating the tracker, whose every enumeration runs change detection.
    /// </summary>
    internal IReadOnlyList<EntityEntry>? TrackedEntries { get; set; }

    /// <summary>The owners in <see cref="TrackedEntries"/> indexed by key, built when an owned entry is first guarded.</summary>
    internal HeadlessOwnerIndex? OwnerIndex { get; set; }

    internal ExceptionDispatchInfo? NonRetryableFailure { get; set; }

    /// <summary>
    /// Entities the processors already ran for, built only when a recollection pass needs to tell new entries from
    /// processed ones.
    /// </summary>
    internal HashSet<object>? ProcessedEntities { get; set; }

    internal Dictionary<IDomainEventEmitter, HashSet<string>> CapturedDomainIdsByEmitter =>
        field ??= new(ReferenceEqualityComparer.Instance);

    internal Dictionary<IIntegrationEventEmitter, HashSet<string>> CapturedIntegrationIdsByEmitter =>
        field ??= new(ReferenceEqualityComparer.Instance);

    internal IReadOnlyList<EventContext<object>> PendingDomainEvents =>
        _pendingDomainEvents ?? (IReadOnlyList<EventContext<object>>)[];

    internal void AddDomainEventEmitter(EmitterDomainEvents emitter) => (_domainEventEmitters ??= []).Add(emitter);

    internal void AddIntegrationEventEmitter(EmitterIntegrationEvents emitter) =>
        (_integrationEventEmitters ??= []).Add(emitter);

    /// <summary>Queues an occurrence for dispatch unless another emitter already queued the same one.</summary>
    internal void QueueDomainEvent(EventContext<object> occurrence)
    {
        if ((_queuedDomainIds ??= new(StringComparer.Ordinal)).Add(occurrence.EventId))
        {
            (_pendingDomainEvents ??= []).Add(occurrence);
        }
    }

    /// <summary>
    /// The current actor's user and account ids, read from <paramref name="currentUser"/> once per save: an
    /// <see cref="ICurrentUser"/> may re-scan its claims on every read.
    /// </summary>
    internal (UserId? UserId, AccountId? AccountId) GetActor(ICurrentUser currentUser) =>
        _actor ??= (currentUser.UserId, currentUser.AccountId);

    internal int DomainEventCursor { get; set; }

    /// <summary>
    /// Whether a domain-event handler may have run during this save's drain, and so changed the tracked graph after the
    /// first snapshot and audit capture.
    /// </summary>
    internal bool HandlerMayHaveChangedGraph { get; set; }

    /// <summary>Whether a domain-event handler may have run since <see cref="TrackedEntries"/> was taken.</summary>
    internal bool GraphChangedSinceSnapshot { get; set; }

    /// <summary>
    /// Answers whether a domain-event payload type has a registered handler, or <see langword="null"/> when the
    /// dispatcher cannot tell, in which case every payload counts as handled.
    /// </summary>
    internal Func<Type, bool>? DomainEventHandlerFilter { get; init; }

    /// <summary>Whether dispatching a payload of <paramref name="payloadType"/> could reach a handler.</summary>
    internal bool HasDomainEventHandler(Type payloadType) => DomainEventHandlerFilter?.Invoke(payloadType) ?? true;

    /// <summary>Clears the event buffers on each captured emitter once they have been dispatched.</summary>
    internal void ClearEmitterMessages()
    {
        if (_integrationEventEmitters is not null)
        {
            foreach (var emitter in _integrationEventEmitters)
            {
                emitter.Emitter.ClearIntegrationEvents(emitter.Events);
            }
        }

        if (_domainEventEmitters is not null)
        {
            foreach (var emitter in _domainEventEmitters)
            {
                emitter.Emitter.ClearDomainEvents(emitter.Events);
            }
        }
    }
}
