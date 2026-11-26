// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
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
    /// Gets the 32-character lowercase hex trace id of the span that caused the innermost open scope's work (the span
    /// that published the consumed message or scheduled the running job), or <see langword="null"/> when that work
    /// stored no trace context or no scope is open.
    /// </summary>
    /// <remarks>
    /// The work's own trace does not lead back to its cause: a job runs in a trace of its own that only links to its
    /// scheduler, and an in-process delivery runs under the consumer loop's activity. Log enrichment writes this value
    /// so the cause's entries and the work's entries can be found by one trace id.
    /// </remarks>
    public static string? CausationTraceId => _Current.Value?.CausationTraceId;

    /// <summary>
    /// Opens a scope for the work of one object. Disposing it restores the scope that was current before.
    /// </summary>
    /// <param name="correlationId">
    /// The business correlation of the work. A blank id, or one longer than <see cref="IdMaxLength"/>, opens a scope
    /// with no correlation: <see cref="Current"/> is <see langword="null"/> inside it, so the inner work takes its own
    /// correlation instead of failing on an id it cannot store, and never inherits an outer scope that is not its own.
    /// </param>
    /// <param name="causationId">The id of the object whose work runs inside the scope.</param>
    /// <param name="causationTraceId">
    /// The trace id of the span that caused the work, read from the trace context the work stored. The default value
    /// means the work stored none; an inner scope never inherits an outer scope's value.
    /// </param>
    /// <returns>A handle that closes the scope. Dispose handles in the reverse order they were opened.</returns>
    /// <exception cref="ArgumentException"><paramref name="causationId"/> is blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="causationId"/> is longer than <see cref="IdMaxLength"/>.
    /// </exception>
    public static IDisposable Begin(
        string? correlationId,
        string causationId,
        ActivityTraceId causationTraceId = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(causationId);
        Argument.HasMaxLength(causationId, IdMaxLength);

        var context =
            string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > IdMaxLength
                ? null
                : new CausalContext(correlationId, causationId);

        // Formatted once per scope, not once per log entry that reads it.
        var traceId = causationTraceId == default ? null : causationTraceId.ToHexString();
        var scope = new Scope(context, traceId, _Current.Value);
        _Current.Value = scope;
        return scope;
    }

    private sealed class Scope(CausalContext? context, string? causationTraceId, Scope? parent) : IDisposable
    {
        private bool _disposed;

        public CausalContext? Context { get; } = context;

        public string? CausationTraceId { get; } = causationTraceId;

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
