// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Reliability;

/// <summary>
/// Declares how a message consumer or a job reacts to failure: how many times it retries at once, how many times it
/// retries after a growing delay, and which exceptions end the failure without any retry.
/// </summary>
/// <remarks>
/// <para>
/// Derive a sealed class with a public parameterless constructor and describe the policy in
/// <see cref="Configure"/>. A handler names the policy by type in its declaration, so a misspelled policy fails the
/// build instead of a lookup at runtime.
/// </para>
/// <para>
/// The runtime calls <see cref="Build"/> once per handler identity and caches the resulting
/// <see cref="FailurePolicyDefinition"/>, so <see cref="Configure"/> must describe the same policy on every call and
/// must not depend on per-message or per-run state.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class PaymentsFailurePolicy : FailurePolicy
/// {
///     protected override void Configure(FailurePolicyBuilder policy) =>
///         policy
///             .Immediate(retries: 2)
///             .Delayed(retries: 5, initialDelay: TimeSpan.FromSeconds(30), maxDelay: TimeSpan.FromMinutes(15))
///             .FailOn&lt;CardDeclinedException&gt;();
/// }
/// </code>
/// </example>
[PublicAPI]
public abstract class FailurePolicy
{
    /// <summary>Describes the policy on <paramref name="policy"/>.</summary>
    /// <param name="policy">The builder that collects the retry tiers and fail rules.</param>
    protected abstract void Configure(FailurePolicyBuilder policy);

    /// <summary>Runs <see cref="Configure"/> on a fresh builder and returns the immutable definition it describes.</summary>
    /// <returns>A definition that is safe to cache and share across threads.</returns>
    /// <exception cref="ArgumentException">The configured retry counts or delays are out of range.</exception>
    public FailurePolicyDefinition Build()
    {
        var builder = new FailurePolicyBuilder();
        Configure(builder);

        return builder.Build();
    }
}
