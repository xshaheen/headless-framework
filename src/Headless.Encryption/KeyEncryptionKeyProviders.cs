// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;

namespace Headless.Encryption;

/// <summary>
/// The active key-encryption-key provider, which wraps every new data key, and the previous providers kept to unwrap
/// keys they wrapped until <see cref="IDataKeyManager.RewrapAsync" /> moves those keys to the active one.
/// </summary>
internal sealed class KeyEncryptionKeyProviders
{
    private readonly FrozenDictionary<string, IKeyEncryptionKeyProvider> _byName;

    public KeyEncryptionKeyProviders(
        IKeyEncryptionKeyProvider active,
        IEnumerable<PreviousKeyEncryptionKeyProvider> previous
    )
    {
        Active = active;

        var byName = new Dictionary<string, IKeyEncryptionKeyProvider>(StringComparer.Ordinal)
        {
            [active.Name] = active,
        };

        foreach (var provider in previous.Select(static p => p.Provider))
        {
            if (!byName.TryAdd(provider.Name, provider))
            {
                throw new InvalidOperationException(
                    $"Two key-encryption-key providers are named '{provider.Name}'. A previous provider must differ "
                        + "from the active one and from every other previous one; to rotate keys within one provider, "
                        + "use that provider's own key versions."
                );
            }
        }

        _byName = byName.ToFrozenDictionary(StringComparer.Ordinal);
    }

    public IKeyEncryptionKeyProvider Active { get; }

    /// <summary>Returns the provider that wrapped keys under <paramref name="name" />, or <see langword="null" />.</summary>
    public IKeyEncryptionKeyProvider? Find(string name) => _byName.GetValueOrDefault(name);
}

/// <summary>A key-encryption-key provider registered only to unwrap the keys it wrapped earlier.</summary>
internal sealed class PreviousKeyEncryptionKeyProvider(IKeyEncryptionKeyProvider provider)
{
    public IKeyEncryptionKeyProvider Provider { get; } = provider;
}
