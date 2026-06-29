// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>
/// Specifies the relative execution order of a <c>IDomainEventHandler&lt;TEvent&gt;</c> implementation
/// when multiple handlers are registered for the same event type.
/// </summary>
/// <param name="order">Desired execution position; handlers with lower values run earlier.</param>
/// <remarks>Handlers are invoked in ascending <c>Order</c> value. Handlers without this attribute are unordered.</remarks>
[PublicAPI]
[AttributeUsage(AttributeTargets.Class)]
public sealed class DomainEventHandlerOrderAttribute(int order) : Attribute
{
    /// <summary>Relative execution order. Handlers with lower values execute first.</summary>
    public int Order { get; } = order;
}
