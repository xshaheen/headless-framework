// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>
/// Specifies the relative execution order of an <see cref="IDomainEventHandler{TPayload}"/> implementation
/// when multiple handlers are registered for the same event type.
/// </summary>
/// <param name="order">The relative execution position. Lower values execute earlier.</param>
/// <remarks>Handlers are invoked in ascending order. Handlers without this attribute execute with default order zero.</remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class)]
public sealed class DomainEventHandlerOrderAttribute(int order) : Attribute
{
    /// <summary>Gets the relative execution order value. Handlers with lower values execute first.</summary>
    public int Order { get; } = order;
}
