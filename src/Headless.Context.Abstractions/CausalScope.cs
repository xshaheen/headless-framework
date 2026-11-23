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

    private static readonly AsyncLocal<CausalContext?> _Current = new();

    /// <summary>Gets the innermost open scope, or <see langword="null"/> outside every scope.</summary>
    public static CausalContext? Current => _Current.Value;

    /// <summary>
    /// Opens a scope for the work of one object. Disposing it restores the scope that was current before.
    /// </summary>
    /// <param name="correlationId">
    /// The business correlation of the work. A blank id, or one longer than <see cref="IdMaxLength"/>, opens nothing,
    /// so the inner work takes its own correlation instead of failing on an id it cannot store.
    /// </param>
    /// <param name="causationId">The id of the object whose work runs inside the scope.</param>
    /// <returns>A handle that closes the scope.</returns>
    /// <exception cref="ArgumentException"><paramref name="causationId"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="causationId"/> is longer than <see cref="IdMaxLength"/>.
    /// </exception>
    public static IDisposable Begin(string? correlationId, string causationId)
    {
        Argument.IsNotNullOrWhiteSpace(causationId);
        Argument.HasMaxLength(causationId, IdMaxLength);

        if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > IdMaxLength)
        {
            return NoScope.Instance;
        }

        var previous = _Current.Value;
        _Current.Value = new CausalContext(correlationId, causationId);
        return new Scope(previous);
    }

    private sealed class Scope(CausalContext? previous) : IDisposable
    {
        public void Dispose() => _Current.Value = previous;
    }

    private sealed class NoScope : IDisposable
    {
        public static readonly NoScope Instance = new();

        public void Dispose() { }
    }
}
