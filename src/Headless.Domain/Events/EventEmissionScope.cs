// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Domain;

/// <summary>Provides nestable asynchronous-flow-local business lineage.</summary>
/// <remarks>Dispose in strict reverse creation order in the owning flow.</remarks>
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

    /// <summary>Gets the current immutable business lineage, or <see langword="null"/> outside an active scope.</summary>
    public static EventEmissionContext? Current => _Current.Value?._context;

    /// <summary>Establishes the parent lineage for newly raised occurrences in this asynchronous flow.</summary>
    /// <param name="context">The event emission context.</param>
    /// <returns>A new <see cref="EventEmissionScope"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public static EventEmissionScope Begin(EventEmissionContext context) => new(context);

    /// <summary>Establishes an existing occurrence as the immediate cause of subsequent emissions.</summary>
    /// <typeparam name="TPayload">The event payload type.</typeparam>
    /// <param name="parent">The parent event context.</param>
    /// <returns>A new <see cref="EventEmissionScope"/> instance.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="parent"/> is <see langword="null"/>.</exception>
    public static EventEmissionScope Begin<TPayload>(EventContext<TPayload> parent)
        where TPayload : class
    {
        Argument.IsNotNull(parent);
        return new(new(parent.CorrelationId, parent.EventId, parent.TenantId));
    }

    /// <summary>Restores the enclosing scope.</summary>
    /// <exception cref="InvalidOperationException">The scope is not the current scope in this asynchronous flow.</exception>
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
