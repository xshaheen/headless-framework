// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Headless.EntityFramework;

#pragma warning disable MA0048 // A topic file: its types are peers with no main type, so the file is named for the topic.
/// <summary>
/// Pairs an emitter with the unique array owned by the collector for this save. The collector snapshots
/// the source buffer and records per-emitter membership before constructing this bookkeeping record.
/// </summary>
internal sealed record EmitterDomainEvents(IDomainEventEmitter Emitter, IReadOnlyList<EventContext<object>> Events);

/// <summary>
/// Retains the collector-owned integration occurrence array for dispatch and exact saved-batch clearing.
/// </summary>
internal sealed record EmitterIntegrationEvents(
    IIntegrationEventEmitter Emitter,
    IReadOnlyList<EventContext<object>> Events
);
