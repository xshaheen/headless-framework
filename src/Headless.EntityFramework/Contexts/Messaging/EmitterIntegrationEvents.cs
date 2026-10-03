// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Domain;

namespace Headless.EntityFramework;

/// <summary>
/// Retains the collector-owned integration occurrence array for dispatch and exact saved-batch clearing.
/// </summary>
internal sealed record EmitterIntegrationEvents(
    IIntegrationEventEmitter Emitter,
    IReadOnlyList<EventContext<object>> Events
);
