// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.EntityFramework;

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
