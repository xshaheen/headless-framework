// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.DistributedLocks;

/// <summary>Exception raised when acquiring a distributed lock exceeds the configured timeout.</summary>
/// <remarks>
/// This exception intentionally inherits <see cref="DistributedLockException"/> rather than
/// <see cref="TimeoutException"/>. Callers writing <c>catch (TimeoutException)</c> will NOT catch
/// this exception — they must catch <see cref="LockAcquisitionTimeoutException"/> directly or its
/// base <see cref="DistributedLockException"/>. The hierarchy preserves room for additional
/// lock-specific exceptions such as <see cref="LockHandleLostException"/> under the same
/// base, while keeping lock-acquisition timeouts distinct from generic I/O-style timeouts.
/// </remarks>
[PublicAPI]
public sealed class LockAcquisitionTimeoutException : DistributedLockException
{
    // Validation runs as part of the chained-ctor argument evaluation (left-to-right) so an
    // invalid `resource` throws before the message is interpolated and before the base ctor
    // runs — avoids constructing a misleading "for resource ''" string just to discard it.

    /// <summary>Initializes the exception with the default timeout message for <paramref name="resource"/>.</summary>
    /// <param name="resource">The resource whose lock acquisition timed out.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or whitespace.</exception>
    public LockAcquisitionTimeoutException(string resource)
        : this(
            Argument.IsNotNullOrWhiteSpace(resource),
            $"Unable to acquire distributed lock for resource '{resource}' before the timeout elapsed."
        ) { }

    /// <summary>Initializes the exception with a custom <paramref name="message"/>.</summary>
    /// <param name="resource">The resource whose lock acquisition timed out.</param>
    /// <param name="message">The error message, or <see langword="null"/> for the default.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or whitespace.</exception>
    public LockAcquisitionTimeoutException(string resource, string? message)
        : base(message)
    {
        Resource = Argument.IsNotNullOrWhiteSpace(resource);
    }

    /// <summary>Initializes the exception with a custom <paramref name="message"/> and inner cause.</summary>
    /// <param name="resource">The resource whose lock acquisition timed out.</param>
    /// <param name="message">The error message, or <see langword="null"/> for the default.</param>
    /// <param name="innerException">The underlying cause, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or whitespace.</exception>
    public LockAcquisitionTimeoutException(string resource, string? message, Exception? innerException)
        : base(message, innerException)
    {
        Resource = Argument.IsNotNullOrWhiteSpace(resource);
    }

    /// <summary>The resource whose lock acquisition timed out.</summary>
    public string Resource { get; }

    /// <summary>
    /// Throw shape for the <c>acquireTimeout: TimeSpan.Zero</c> fast-path. The single storage
    /// attempt observed contention; the caller asked for try-once semantics, so there is no
    /// retry loop to surface as a "timeout elapsed" message.
    /// </summary>
    /// <param name="resource">The resource whose lock acquisition was attempted once and failed.</param>
    /// <returns>A <see cref="LockAcquisitionTimeoutException"/> describing the try-once contention failure.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="resource"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="resource"/> is empty or whitespace.</exception>
    public static LockAcquisitionTimeoutException ForTryOnceContention(string resource)
    {
        return new LockAcquisitionTimeoutException(
            Argument.IsNotNullOrWhiteSpace(resource),
            $"Failed to acquire distributed lock on '{resource}' on the first attempt (try-once contention)."
        );
    }
}
