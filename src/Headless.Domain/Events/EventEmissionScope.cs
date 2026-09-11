// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Domain;

/// <summary>Nestable async-flow-local business lineage. Dispose in strict reverse creation order in the owning flow.</summary>
[PublicAPI]
public sealed class EventEmissionScope : IDisposable
{
    private static readonly AsyncLocal<EventEmissionScope?> _Current = new();
    private readonly EventEmissionScope? _parent;
    private readonly EventEmissionContext _context;

    private EventEmissionScope(EventEmissionContext context)
    {
        _context = Argument.IsNotNull(context);
        _parent = _Current.Value;
        _Current.Value = this;
    }

    /// <summary>The current immutable business lineage; null outside a scope.</summary>
    public static EventEmissionContext? Current => _Current.Value?._context;

    /// <summary>Establishes the parent lineage for newly raised occurrences in this async flow.</summary>
    public static EventEmissionScope Begin(EventEmissionContext context) => new(context);

    /// <summary>Establishes an existing occurrence as the immediate cause of subsequent emissions.</summary>
    public static EventEmissionScope Begin<TPayload>(EventContext<TPayload> parent)
        where TPayload : class
    {
        Argument.IsNotNull(parent);
        return new(new(parent.CorrelationId, parent.EventId, parent.TenantId));
    }

    /// <summary>Restores the enclosing scope. Disposal is strict and cannot be repeated.</summary>
    /// <exception cref="InvalidOperationException">The scope is not the current scope in this async flow.</exception>
#pragma warning disable CA1065 // Strict LIFO rejection prevents silently corrupting ambient business lineage.
    public void Dispose()
    {
        if (!ReferenceEquals(_Current.Value, this))
        {
            throw new InvalidOperationException(
                "Event emission scopes must be disposed in strict LIFO order in their owning async flow."
            );
        }

        _Current.Value = _parent;
    }
#pragma warning restore CA1065
}
