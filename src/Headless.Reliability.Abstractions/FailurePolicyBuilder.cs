// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Reliability;

/// <summary>
/// Collects the retry tiers and fail rules of a failure policy and builds them into an immutable
/// <see cref="FailurePolicyDefinition"/>.
/// </summary>
/// <remarks>
/// Each method validates its arguments when called, so an out-of-range value fails where it is written. Calling
/// <see cref="Immediate"/> or <see cref="Delayed"/> again replaces the earlier tier; fail rules accumulate. A builder
/// with no calls describes a policy with no retries and no fail rules.
/// </remarks>
[PublicAPI]
public sealed class FailurePolicyBuilder
{
    private readonly List<Type> _failOnTypes = [];
    private readonly List<Func<Exception, bool>> _failWhenPredicates = [];
    private int _immediateRetries;
    private int _delayedRetries;
    private TimeSpan _delayedInitialDelay;
    private TimeSpan _delayedMaxDelay;

    /// <summary>Sets how many times a failure is retried back-to-back, with no delay, before any delayed retry.</summary>
    /// <param name="retries">The immediate retry count, from 0 to <see cref="FailurePolicyDefinition.MaxRetriesPerTier"/>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="retries"/> is out of range.</exception>
    public FailurePolicyBuilder Immediate(int retries)
    {
        FailurePolicyDefinition.ValidateRetries(retries, nameof(retries));
        _immediateRetries = retries;

        return this;
    }

    /// <summary>
    /// Sets how many times a failure is retried after a delay once the immediate retries are spent. Delayed retry
    /// <c>n</c> waits <c>min(initialDelay × 2^(n-1), maxDelay)</c> with jitter; see
    /// <see cref="FailurePolicyDefinition.GetDelayedRetryDelay(int)"/>.
    /// </summary>
    /// <param name="retries">The delayed retry count, from 0 to <see cref="FailurePolicyDefinition.MaxRetriesPerTier"/>.</param>
    /// <param name="initialDelay">The delay before the first delayed retry. Must be positive when <paramref name="retries"/> is positive.</param>
    /// <param name="maxDelay">The cap on any delay. Must be at least <paramref name="initialDelay"/>.</param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A count or delay is out of range.</exception>
    /// <remarks>Both delays are at most <see cref="FailurePolicyDefinition.MaxDelayLimit"/>.</remarks>
    public FailurePolicyBuilder Delayed(int retries, TimeSpan initialDelay, TimeSpan maxDelay)
    {
        FailurePolicyDefinition.ValidateRetries(retries, nameof(retries));
        FailurePolicyDefinition.ValidateDelays(retries, initialDelay, maxDelay, nameof(initialDelay), nameof(maxDelay));

        _delayedRetries = retries;
        _delayedInitialDelay = initialDelay;
        _delayedMaxDelay = maxDelay;

        return this;
    }

    /// <summary>
    /// Ends the failure at once, skipping every remaining retry, when the exception is
    /// <typeparamref name="TException"/> or a type derived from it.
    /// </summary>
    /// <typeparam name="TException">The exception type that is never worth retrying.</typeparam>
    /// <returns>This builder.</returns>
    public FailurePolicyBuilder FailOn<TException>()
        where TException : Exception
    {
        if (!_failOnTypes.Contains(typeof(TException)))
        {
            _failOnTypes.Add(typeof(TException));
        }

        return this;
    }

    /// <summary>Ends the failure at once, skipping every remaining retry, when <paramref name="predicate"/> returns <see langword="true"/>.</summary>
    /// <param name="predicate">
    /// A side-effect-free test of the exception. It runs on every failure and may run concurrently. If it throws,
    /// the failure is treated as matched; see <see cref="FailurePolicyDefinition.ShouldFail(Exception, out Exception?)"/>.
    /// </param>
    /// <returns>This builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public FailurePolicyBuilder FailWhen(Func<Exception, bool> predicate)
    {
        Argument.IsNotNull(predicate);
        _failWhenPredicates.Add(predicate);

        return this;
    }

    /// <summary>Builds the immutable definition this builder describes. The builder can keep being used afterwards.</summary>
    /// <returns>A definition that is safe to cache and share across threads.</returns>
    public FailurePolicyDefinition Build()
    {
        return new FailurePolicyDefinition(
            _immediateRetries,
            _delayedRetries,
            _delayedInitialDelay,
            _delayedMaxDelay,
            [.. _failOnTypes],
            [.. _failWhenPredicates]
        );
    }
}
