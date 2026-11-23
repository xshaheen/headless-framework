// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Context;

/// <summary>
/// The ambient causal scope a job run or a message consume opens around application code, so work that code starts
/// (a published message, a scheduled job, a domain event, an audit row) joins the same business operation without
/// the code passing ids by hand.
/// </summary>
/// <remarks>
/// The scope flows with the async flow like any <see cref="AsyncLocal{T}"/>: a task started inside it sees it, and
/// framework loops started at host start run outside every scope. An explicit id on a publish or a schedule always
/// wins over the scope.
/// </remarks>
[PublicAPI]
public static class CausalScope
{
    /// <summary>
    /// Maximum correlation-id and causation-id length in UTF-16 code units. Stores index the correlation id so one
    /// business operation can be listed, and an id past this length cannot be indexed on every engine.
    /// </summary>
    public const int IdMaxLength = 200;

    private static readonly AsyncLocal<Scope?> _Current = new();

    /// <summary>Gets the innermost open scope, or <see langword="null"/> outside every scope.</summary>
    public static CausalContext? Current => _Current.Value?.Context;

    /// <summary>
    /// Opens a scope for the work of one object. Disposing it restores the scope that was current before.
    /// </summary>
    /// <param name="correlationId">
    /// The business correlation of the work. A blank id, or one longer than <see cref="IdMaxLength"/>, opens a scope
    /// with no correlation: <see cref="Current"/> is <see langword="null"/> inside it, so the inner work takes its own
    /// correlation instead of failing on an id it cannot store, and never inherits an outer scope that is not its own.
    /// </param>
    /// <param name="causationId">The id of the object whose work runs inside the scope.</param>
    /// <returns>A handle that closes the scope. Dispose handles in the reverse order they were opened.</returns>
    /// <exception cref="ArgumentException"><paramref name="causationId"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="causationId"/> is longer than <see cref="IdMaxLength"/>.
    /// </exception>
    public static IDisposable Begin(string? correlationId, string causationId)
    {
        Argument.IsNotNullOrWhiteSpace(causationId);
        Argument.HasMaxLength(causationId, IdMaxLength);

        var context =
            string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > IdMaxLength
                ? null
                : new CausalContext(correlationId, causationId);

        var scope = new Scope(context, _Current.Value);
        _Current.Value = scope;
        return scope;
    }

    private sealed class Scope(CausalContext? context, Scope? parent) : IDisposable
    {
        private bool _disposed;

        public CausalContext? Context { get; } = context;

#pragma warning disable CA1065 // Strict LIFO rejection prevents silently restoring the wrong business lineage.
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (!ReferenceEquals(_Current.Value, this))
            {
                throw new InvalidOperationException(
                    "Causal scopes must be disposed in reverse order of opening, in the async flow that opened them."
                );
            }

            _disposed = true;
            _Current.Value = parent;
        }
#pragma warning restore CA1065
    }
}
