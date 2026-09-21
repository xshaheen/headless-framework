// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Settings.Values;

/// <summary>
/// Message published over <c>IBus</c> by <c>SettingManager</c> after a write, so peer instances holding a
/// resolved setting value learn that it is stale. Consumers subscribe with
/// <c>IConsume&lt;SettingChangedMessage&gt;</c> and re-read the names they care about.
/// </summary>
/// <remarks>
/// <para>
/// This is a "re-read these names" signal, not a value feed. Carrying values would put the plaintext of an
/// <c>IsEncrypted</c> setting on the broker, and it would make delivery order load-bearing: two writes racing
/// could leave a receiver holding the older one. Re-reading is idempotent and order-free, at the cost of one
/// read per instance per change, which is the read those instances were already doing on every poll.
/// </para>
/// <para>
/// The cache is a separate concern and is already handled. A store-backed write evicts its own cache entry, and
/// a hybrid cache broadcasts that eviction through <c>CacheInvalidationMessage</c>. This message exists for
/// state the framework cannot see: a value a consumer copied into a field of its own.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record SettingChangedMessage
{
    /// <summary>
    /// ID of the instance that originated this change. Receivers whose own instance ID matches skip the
    /// message, because the writer already has the new value.
    /// </summary>
    public required string InstanceId { get; init; }

    /// <summary>
    /// Names of the settings whose stored value changed. Never empty. A delete that clears a whole provider
    /// scope reports every name it removed rather than a wildcard, so a consumer can match on the names it
    /// holds without knowing the provider's contents.
    /// </summary>
    public required IReadOnlyList<string> SettingNames { get; init; }

    /// <summary>
    /// The provider that stored the change, for example <see cref="SettingValueProviderNames.Global"/>.
    /// </summary>
    /// <remarks>
    /// Load-bearing for a consumer that caches application-wide policy: a write at a narrower scope, such as one
    /// user's override, must not make it discard a Global value that did not change.
    /// </remarks>
    public required string ProviderName { get; init; }

    /// <summary>
    /// The provider-specific discriminator the change was written under, such as a tenant or user ID, or
    /// <see langword="null"/> for a provider that has no key.
    /// </summary>
    public string? ProviderKey { get; init; }

    /// <summary>
    /// UTC timestamp stamped by the originating instance at publish. A receiver comparing two messages for the
    /// same name uses this rather than arrival order, which a broker does not guarantee.
    /// </summary>
    public DateTimeOffset? Timestamp { get; init; }
}
