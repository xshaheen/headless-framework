// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Marker interface for in-process domain events raised by aggregates and dispatched
/// within the current unit of work via <c>ILocalEventBus</c>.
/// </summary>
[PublicAPI]
public interface IDomainEvent;
