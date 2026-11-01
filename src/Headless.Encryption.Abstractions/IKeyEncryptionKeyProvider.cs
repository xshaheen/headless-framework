// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Encryption;

/// <summary>
/// Wraps and unwraps data keys with a key-encryption key (KEK) that stays in a key-management service, an HSM, or the
/// host's configuration. Headless stores only wrapped data keys and never creates a KEK.
/// </summary>
/// <remarks>
/// <para>
/// Implemented by key-encryption-key provider packages (the local key in <c>Headless.Encryption</c>, Azure Key
/// Vault, and others), and open to third parties: everything a provider needs is in this interface. Register one
/// through the provider's <c>Use…</c> member on the encryption setup builder; exactly one provider is allowed.
/// </para>
/// <para>
/// New members arrive as default interface members, so an existing provider keeps compiling.
/// </para>
/// </remarks>
[PublicAPI]
public interface IKeyEncryptionKeyProvider
{
    /// <summary>
    /// Gets the provider's stable name, stored with every key it wraps, such as <c>Local</c> or <c>AzureKeyVault</c>.
    /// At most <see cref="DataKeyFieldLimits.KekProviderMaxLength" /> characters.
    /// </summary>
    string Name { get; }

    /// <summary>Wraps <paramref name="dataKey" /> with the current version of the key-encryption key.</summary>
    /// <param name="dataKey">The 32-byte data key. The provider must not keep a reference to it.</param>
    /// <param name="context">The data key's identity, bound to the wrapped key where the provider supports it.</param>
    /// <param name="cancellationToken">Cancels the call to the key service.</param>
    /// <returns>The wrapped key and the id of the key-encryption-key version that wrapped it.</returns>
    ValueTask<WrappedDataKey> WrapAsync(
        ReadOnlyMemory<byte> dataKey,
        KeyWrapContext context,
        CancellationToken cancellationToken
    );

    /// <summary>Unwraps <paramref name="wrapped" /> with the key-encryption-key version its id names.</summary>
    /// <param name="wrapped">The wrapped key as <see cref="WrapAsync" /> returned it.</param>
    /// <param name="context">The same context it was wrapped with.</param>
    /// <param name="cancellationToken">Cancels the call to the key service.</param>
    /// <returns>The data key. The caller owns the array and clears it.</returns>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The wrapped key or the context was changed, or the key-encryption-key version is unknown or disabled.
    /// </exception>
    ValueTask<byte[]> UnwrapAsync(WrappedDataKey wrapped, KeyWrapContext context, CancellationToken cancellationToken);
}
