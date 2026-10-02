// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using Headless.Checks;

namespace Headless.Reliability;

/// <summary>The immutable, built form of a <see cref="FailurePolicy"/> that a runtime executes.</summary>
/// <remarks>
/// <para>
/// A failure is attempted at most <see cref="TotalAttempts"/> times: the first attempt, then
/// <see cref="ImmediateRetries"/> back-to-back retries, then <see cref="DelayedRetries"/> retries that each wait
/// <see cref="GetDelayedRetryDelay(int)"/>. A failure that <see cref="ShouldFail(Exception)"/> matches skips every
/// remaining retry.
/// </para>
/// <para>
/// Instances hold no mutable state and are safe to cache and share across threads. Create one with
/// <see cref="FailurePolicy.Build"/> or <see cref="FailurePolicyBuilder.Build"/>.
/// </para>
/// </remarks>
[PublicAPI]
public sealed class FailurePolicyDefinition
{
    /// <summary>The largest retry count either tier accepts.</summary>
    public const int MaxRetriesPerTier = 100;

    /// <summary>The fraction of the computed delay that jitter may add or remove.</summary>
    public const double JitterFraction = 0.2;

    /// <summary>The largest initial or maximum delay a policy accepts.</summary>
    public static readonly TimeSpan MaxDelayLimit = TimeSpan.FromHours(24);

    /// <summary>A policy with no retries and no fail rules: the first failure is terminal.</summary>
    public static FailurePolicyDefinition None { get; } = new FailurePolicyBuilder().Build();

    private readonly ImmutableArray<Type> _failOnTypes;
    private readonly ImmutableArray<Func<Exception, bool>> _failWhenPredicates;

    internal FailurePolicyDefinition(
        int immediateRetries,
        int delayedRetries,
        TimeSpan delayedInitialDelay,
        TimeSpan delayedMaxDelay,
        ImmutableArray<Type> failOnTypes,
        ImmutableArray<Func<Exception, bool>> failWhenPredicates
    )
    {
        ValidateRetries(immediateRetries, nameof(ImmediateRetries));
        ValidateRetries(delayedRetries, nameof(DelayedRetries));
        ValidateDelays(
            delayedRetries,
            delayedInitialDelay,
            delayedMaxDelay,
            nameof(DelayedInitialDelay),
            nameof(DelayedMaxDelay)
        );

        ImmediateRetries = immediateRetries;
        DelayedRetries = delayedRetries;
        DelayedInitialDelay = delayedInitialDelay;
        DelayedMaxDelay = delayedMaxDelay;
        _failOnTypes = failOnTypes;
        _failWhenPredicates = failWhenPredicates;
    }

    /// <summary>How many times a failure is retried back-to-back, with no delay, before any delayed retry.</summary>
    public int ImmediateRetries { get; }

    /// <summary>How many times a failure is retried after a delay once the immediate retries are spent.</summary>
    public int DelayedRetries { get; }

    /// <summary>The delay before the first delayed retry, before jitter.</summary>
    public TimeSpan DelayedInitialDelay { get; }

    /// <summary>The cap on any delayed retry's delay, jitter included.</summary>
    public TimeSpan DelayedMaxDelay { get; }

    /// <summary>The most attempts a failing operation gets: <c>1 + ImmediateRetries + DelayedRetries</c>.</summary>
    public int TotalAttempts => 1 + ImmediateRetries + DelayedRetries;

    /// <summary>The exception types that <see cref="FailurePolicyBuilder.FailOn{TException}"/> declared, in declaration order.</summary>
    public IReadOnlyList<Type> FailOnExceptionTypes => _failOnTypes;

    /// <summary>The number of predicates that <see cref="FailurePolicyBuilder.FailWhen"/> declared.</summary>
    public int FailWhenRuleCount => _failWhenPredicates.Length;

    /// <summary>
    /// Returns the delay of delayed retry <paramref name="delayedAttempt"/> before jitter:
    /// <c>min(DelayedInitialDelay × 2^(delayedAttempt-1), DelayedMaxDelay)</c>.
    /// </summary>
    /// <param name="delayedAttempt">The 1-based delayed retry number. Values past <see cref="DelayedRetries"/> are allowed and return the cap once the doubling reaches it.</param>
    /// <returns>The deterministic delay, never above <see cref="DelayedMaxDelay"/>. Use it where jitter cannot be stored, such as a persisted schedule.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delayedAttempt"/> is less than 1.</exception>
    public TimeSpan GetDelayedRetryBaseDelay(int delayedAttempt)
    {
        Argument.IsPositive(delayedAttempt);

        var exponent = delayedAttempt - 1;
        var initialTicks = DelayedInitialDelay.Ticks;
        var maxTicks = DelayedMaxDelay.Ticks;

        // Compare against the cap shifted right instead of shifting the initial delay left, so a large attempt number
        // reaches the cap without ever overflowing a long.
        if (exponent >= 63 || initialTicks > maxTicks >> exponent)
        {
            return DelayedMaxDelay;
        }

        return TimeSpan.FromTicks(initialTicks << exponent);
    }

    /// <summary>
    /// Returns the delay of delayed retry <paramref name="delayedAttempt"/> with jitter drawn from
    /// <see cref="Random.Shared"/>.
    /// </summary>
    /// <param name="delayedAttempt">The 1-based delayed retry number.</param>
    /// <returns>
    /// <see cref="GetDelayedRetryBaseDelay"/> scaled by a uniform factor in
    /// <c>[1 - JitterFraction, 1 + JitterFraction)</c>, then capped at <see cref="DelayedMaxDelay"/>. Jitter spreads
    /// retries of many failures that started together so they do not hit the failing dependency at once.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delayedAttempt"/> is less than 1.</exception>
    public TimeSpan GetDelayedRetryDelay(int delayedAttempt) => GetDelayedRetryDelay(delayedAttempt, Random.Shared);

    /// <summary>Returns the delay of delayed retry <paramref name="delayedAttempt"/> with jitter drawn from <paramref name="random"/>.</summary>
    /// <param name="delayedAttempt">The 1-based delayed retry number.</param>
    /// <param name="random">The jitter source. Its <see cref="Random.NextDouble"/> picks the point in the jitter band; 0.5 yields the base delay.</param>
    /// <returns>The jittered delay, never above <see cref="DelayedMaxDelay"/>; see <see cref="GetDelayedRetryDelay(int)"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="delayedAttempt"/> is less than 1.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="random"/> is <see langword="null"/>.</exception>
    public TimeSpan GetDelayedRetryDelay(int delayedAttempt, Random random)
    {
        Argument.IsNotNull(random);

        var baseTicks = GetDelayedRetryBaseDelay(delayedAttempt).Ticks;
#pragma warning disable CA5394 // False positive: the value only spreads retry delays; nothing security-sensitive depends on it.
        var factor = 1 - JitterFraction + (2 * JitterFraction * random.NextDouble());
#pragma warning restore CA5394
        var jitteredTicks = Math.Round(baseTicks * factor, MidpointRounding.AwayFromZero);

        // The base delay is at most 24 hours, so the jittered tick count stays far inside the long range.
        return TimeSpan.FromTicks(Math.Min((long)jitteredTicks, DelayedMaxDelay.Ticks));
    }

    /// <summary>Returns whether <paramref name="exception"/> ends the failure at once, skipping every remaining retry.</summary>
    /// <param name="exception">The exception the handler threw, already unwrapped from any framework wrapper.</param>
    /// <returns>
    /// <see langword="true"/> when a <see cref="FailurePolicyBuilder.FailOn{TException}"/> type matches the exception
    /// or a subtype of it, or a <see cref="FailurePolicyBuilder.FailWhen"/> predicate returns <see langword="true"/>
    /// or throws; otherwise <see langword="false"/>, meaning the failure is retryable. Use
    /// <see cref="ShouldFail(Exception, out Exception?)"/> to log a predicate that throws.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public bool ShouldFail(Exception exception) => ShouldFail(exception, out _);

    /// <summary>Returns whether <paramref name="exception"/> ends the failure at once, and reports a fail rule that threw.</summary>
    /// <param name="exception">The exception the handler threw, already unwrapped from any framework wrapper.</param>
    /// <param name="ruleException">
    /// The exception a <see cref="FailurePolicyBuilder.FailWhen"/> predicate threw, or <see langword="null"/>. A
    /// predicate that throws counts as matched, because retrying a failure the policy cannot classify risks repeating
    /// a harmful side effect; the caller should log this exception so the broken rule gets fixed.
    /// </param>
    /// <returns>See <see cref="ShouldFail(Exception)"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="exception"/> is <see langword="null"/>.</exception>
    public bool ShouldFail(Exception exception, out Exception? ruleException)
    {
        Argument.IsNotNull(exception);
        ruleException = null;

        foreach (var type in _failOnTypes)
        {
            if (type.IsInstanceOfType(exception))
            {
                return true;
            }
        }

        foreach (var predicate in _failWhenPredicates)
        {
            try
            {
                if (predicate(exception))
                {
                    return true;
                }
            }
#pragma warning disable CA1031 // A fail rule is user code; any throw it raises is reported to the caller, never swallowed.
            catch (Exception e)
#pragma warning restore CA1031
            {
                ruleException = e;

                return true;
            }
        }

        return false;
    }

    /// <summary>Returns a definition with the supplied numeric fields of <paramref name="overrides"/> replaced and the fail rules kept.</summary>
    /// <param name="overrides">The values to replace; a <see langword="null"/> property keeps the current value.</param>
    /// <returns>A new definition, or this instance when <paramref name="overrides"/> sets nothing.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="overrides"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The combined values are out of range, as the builder would reject them.</exception>
    public FailurePolicyDefinition With(FailurePolicyOverrides overrides)
    {
        Argument.IsNotNull(overrides);

        if (
            overrides.ImmediateRetries is null
            && overrides.DelayedRetries is null
            && overrides.DelayedInitialDelay is null
            && overrides.DelayedMaxDelay is null
        )
        {
            return this;
        }

        return new FailurePolicyDefinition(
            overrides.ImmediateRetries ?? ImmediateRetries,
            overrides.DelayedRetries ?? DelayedRetries,
            overrides.DelayedInitialDelay ?? DelayedInitialDelay,
            overrides.DelayedMaxDelay ?? DelayedMaxDelay,
            _failOnTypes,
            _failWhenPredicates
        );
    }

    internal static void ValidateRetries(int retries, string paramName)
    {
        Argument.IsInclusiveBetween(retries, 0, MaxRetriesPerTier, argumentParamName: paramName);
    }

    internal static void ValidateDelays(
        int retries,
        TimeSpan initialDelay,
        TimeSpan maxDelay,
        string initialDelayParamName,
        string maxDelayParamName
    )
    {
        Argument.IsInclusiveBetween(
            initialDelay,
            TimeSpan.Zero,
            MaxDelayLimit,
            argumentParamName: initialDelayParamName
        );

        if (retries > 0)
        {
            Argument.IsPositive(initialDelay, paramName: initialDelayParamName);
        }

        Argument.IsInclusiveBetween(maxDelay, initialDelay, MaxDelayLimit, argumentParamName: maxDelayParamName);
    }
}
