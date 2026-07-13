// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>
/// Exception raised when releasing or disposing a lock reported one or more failures during compensating cleanup.
/// </summary>
/// <remarks>
/// Cleanup is compensating, not transactional: every child is attempted even after one fails, and the failures are
/// surfaced rather than hidden. A resource whose release failed may remain held until its TTL expires (or, for a
/// connection-scoped lock, until the connection is torn down), so treat this as "ownership may still be held" rather
/// than "released".
/// <para>
/// This inherits <see cref="DistributedLockException"/> so that <c>catch (DistributedLockException)</c> — the
/// package's documented catch-all — also catches cleanup failures. Prefer <see cref="Failures"/> over
/// <see cref="Exception.InnerException"/>: the latter carries only the first failure.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class LockCleanupFailedException : DistributedLockException
{
    /// <summary>Initializes an exception carrying every failure reported by cleanup.</summary>
    /// <param name="failures">The failures reported while releasing or disposing. Must be non-empty.</param>
    /// <exception cref="ArgumentNullException"><paramref name="failures"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="failures"/> is empty.</exception>
    public LockCleanupFailedException(IReadOnlyList<Exception> failures)
        : this(failures, _BuildMessage(failures)) { }

    /// <summary>Initializes an exception carrying every failure reported by cleanup, with a custom message.</summary>
    /// <param name="failures">The failures reported while releasing or disposing. Must be non-empty.</param>
    /// <param name="message">The error message, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="failures"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="failures"/> is empty.</exception>
    public LockCleanupFailedException(IReadOnlyList<Exception> failures, string? message)
        : base(message, _FirstOf(failures))
    {
        Failures = [.. failures];
    }

    /// <summary>Every failure reported by cleanup, in the order the children were attempted (reverse acquisition order).</summary>
    public IReadOnlyList<Exception> Failures { get; }

    private static Exception _FirstOf(IReadOnlyList<Exception> failures)
    {
        Argument.IsNotNull(failures);
        Argument.IsNotEmpty(failures);

        return failures[0];
    }

    private static string _BuildMessage(IReadOnlyList<Exception> failures)
    {
        return _FirstOf(failures) is var first && failures.Count == 1
            ? $"Distributed lock cleanup reported a failure: {first.Message}"
            : $"Distributed lock cleanup reported {failures.Count} failures; see {nameof(Failures)}.";
    }
}
