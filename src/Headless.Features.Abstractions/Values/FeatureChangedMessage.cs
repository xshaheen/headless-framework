// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features.Values;

/// <summary>
/// Message published over <c>IBus</c> by <c>FeatureManager</c> after a write, so peer instances holding a
/// resolved feature value learn that it is stale. Consumers subscribe with
/// <c>IConsume&lt;FeatureChangedMessage&gt;</c> and re-read the names they care about.
/// </summary>
/// <remarks>
/// <para>
/// This is a "re-read these names" signal, not a value feed. Carrying values would increase broker payloads
/// and make delivery order load-bearing: two writes racing could leave a receiver holding the older one.
/// Re-reading is idempotent and order-free, and keeps the message useful only as a re-read trigger.
/// </para>
/// <para>
/// The cache is a separate concern and is already handled. A store-backed write updates or evicts its own
/// cache entry, and a distributed cache broadcasts that change through <c>CacheInvalidationMessage</c>. This
/// message exists for state the framework cannot see: a value a consumer copied into a field of its own.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record FeatureChangedMessage
{
    /// <summary>
    /// Names of the features whose stored value changed. Never empty. A delete that clears a whole provider
    /// scope reports every name it removed rather than a wildcard, so a consumer can match on the names it
    /// holds without knowing the provider's contents.
    /// </summary>
    public required IReadOnlyList<string> FeatureNames { get; init; }

    /// <summary>
    /// The provider that stored the change, for example <see cref="FeatureValueProviderNames.Tenant"/>.
    /// </summary>
    /// <remarks>
    /// Load-bearing for a consumer that caches application-wide policy: a write at a narrower scope, such as one
    /// tenant's override, must not make it discard a DefaultValue value that did not change.
    /// </remarks>
    public required string ProviderName { get; init; }

    /// <summary>
    /// The provider-specific discriminator the change was written under, such as a tenant or edition ID, or
    /// <see langword="null"/> for a provider that has no key.
    /// </summary>
    public string? ProviderKey { get; init; }

    /// <summary>
    /// <c>IHostIdentityAccessor.InstanceId</c> of the process that wrote the change, for logs and telemetry.
    /// </summary>
    /// <remarks>
    /// Do not filter on it. The writer's own process holds copies too — the manager that stored the value knows
    /// nothing about the field some consumer copied it into — so the originating instance must re-read like
    /// every other. That is the opposite of a cache invalidation, where the writer already updated its own tier.
    /// </remarks>
    public required string OriginInstanceId { get; init; }
}
