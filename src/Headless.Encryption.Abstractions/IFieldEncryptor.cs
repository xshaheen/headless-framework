// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Encryption;

/// <summary>
/// Encrypts and decrypts field values under the data key of their scope (envelope encryption), so personal data is
/// stored as ciphertext and a tenant's data can be destroyed by deleting its keys.
/// </summary>
/// <remarks>
/// <para>
/// Call it at the service boundary: entities hold the <see cref="EncryptedValue" /> frame, never the plaintext. The
/// first encryption for a scope with no data key creates one; <see cref="IDataKeyManager.EnsureAsync" /> creates it
/// earlier, for example during tenant onboarding.
/// </para>
/// <para>
/// A context with no scope uses the current tenant. With no current tenant, both calls throw
/// <c>Headless.MultiTenancy.MissingTenantContextException</c>; host-level data passes <see cref="FieldContext.Host" />.
/// </para>
/// <para>Implemented by Headless; applications call it and do not implement it.</para>
/// </remarks>
[PublicAPI]
public interface IFieldEncryptor
{
    /// <summary>Encrypts <paramref name="plaintext" /> under the active data key of the context's scope.</summary>
    /// <param name="plaintext">The bytes to encrypt.</param>
    /// <param name="context">The purpose, scope, and optional binding the value is bound to.</param>
    /// <param name="cancellationToken">Cancels the key lookup.</param>
    /// <returns>The self-describing ciphertext frame.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">The context's scope is too long or is not portable key text.</exception>
    ValueTask<EncryptedValue> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        FieldContext context,
        CancellationToken cancellationToken = default
    );

    /// <summary>Decrypts <paramref name="value" /> with the data key its frame names.</summary>
    /// <param name="value">The ciphertext frame.</param>
    /// <param name="context">The same purpose, scope, and binding the value was encrypted with.</param>
    /// <param name="cancellationToken">Cancels the key lookup.</param>
    /// <returns>The plaintext, and whether the frame's key is still the scope's active key.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="value" /> is the default value, or the context's scope is too long or not portable key text.
    /// </exception>
    /// <exception cref="FieldDecryptionException">
    /// The frame's key no longer exists, the context differs from the one the value was encrypted with, the frame was
    /// changed, or its algorithm is unknown. No partial plaintext is ever returned.
    /// </exception>
    ValueTask<FieldDecryption> DecryptAsync(
        EncryptedValue value,
        FieldContext context,
        CancellationToken cancellationToken = default
    );
}
