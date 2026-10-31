// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Encryption;

/// <summary>
/// The maximum lengths of the stored data-key fields. Call validation and every key store's table definition read
/// these, so a value that passes validation always fits its column.
/// </summary>
[PublicAPI]
public static class DataKeyFieldLimits
{
    /// <summary>The maximum length of a key scope (a tenant id, or a longer per-subject scope such as <c>{tenant}/{id}</c>).</summary>
    public const int ScopeMaxLength = 200;

    /// <summary>The maximum length of a key-encryption-key provider's <see cref="IKeyEncryptionKeyProvider.Name" />.</summary>
    public const int KekProviderMaxLength = 64;

    /// <summary>The maximum length of a <see cref="WrappedDataKey.KekId" />, which may be a versioned key URI.</summary>
    public const int KekIdMaxLength = 1000;
}
