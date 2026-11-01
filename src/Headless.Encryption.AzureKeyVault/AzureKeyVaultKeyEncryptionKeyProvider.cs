// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Headless.Checks;

namespace Headless.Encryption.AzureKeyVault;

/// <summary>
/// Wraps data keys with RSA-OAEP-256 under a Key Vault key. RSA-OAEP takes no associated data, so the wrapped payload
/// is the data key followed by a SHA-256 of its context (scope, id, kind), checked on unwrap: a wrapped key moved to
/// another row fails to unwrap.
/// </summary>
/// <remarks>
/// The binding stops a wrapped key from moving between rows. It cannot stop someone who holds the RSA public key from
/// wrapping a key they chose, because wrapping needs only the public key; a symmetric key wrap (Managed HSM) would.
/// </remarks>
internal sealed class AzureKeyVaultKeyEncryptionKeyProvider(
    KeyClient client,
    AzureKeyVaultKeyEncryptionKeyOptions options
) : IKeyEncryptionKeyProvider
{
    public const string ProviderName = "AzureKeyVault";

    private const int _DataKeyLength = 32;
    private const int _ContextHashLength = 32;

    private readonly KeyClient _client = Argument.IsNotNull(client);
    private readonly string _keyName = Argument.IsNotNullOrWhiteSpace(Argument.IsNotNull(options).KeyName);

    public string Name => ProviderName;

    private static ReadOnlySpan<byte> ContextLabel => "headless/kek/akv/v1\0"u8;

    public async ValueTask<WrappedDataKey> WrapAsync(
        ReadOnlyMemory<byte> dataKey,
        KeyWrapContext context,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(context);
        Argument.IsTrue(dataKey.Length == _DataKeyLength, $"A data key is {_DataKeyLength} bytes.", nameof(dataKey));

        var payload = new byte[_DataKeyLength + _ContextHashLength];

        try
        {
            dataKey.Span.CopyTo(payload);
            SHA256.HashData(KeyWrapContextEncoding.Encode(ContextLabel, context), payload.AsSpan(_DataKeyLength));

            var result = await _client
                .GetCryptographyClient(_keyName)
                .WrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, payload, cancellationToken)
                .ConfigureAwait(false);

            // The result names the exact key version that wrapped, which unwrap must use after a key rotation.
            return new WrappedDataKey { KekId = result.KeyId, Ciphertext = result.EncryptedKey };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public async ValueTask<byte[]> UnwrapAsync(
        WrappedDataKey wrapped,
        KeyWrapContext context,
        CancellationToken cancellationToken
    )
    {
        Argument.IsNotNull(wrapped);
        Argument.IsNotNull(context);

        if (
            !Uri.TryCreate(wrapped.KekId, UriKind.Absolute, out var kekId)
            || !KeyVaultKeyIdentifier.TryCreate(kekId, out var identifier)
            || identifier.Version is null
        )
        {
            throw new CryptographicException(
                $"The wrapped data key names '{wrapped.KekId}', which is not a versioned Key Vault key identifier."
            );
        }

        if (!string.Equals(identifier.VaultUri.Host, _client.VaultUri.Host, StringComparison.OrdinalIgnoreCase))
        {
            throw new CryptographicException(
                $"The wrapped data key was wrapped in vault '{identifier.VaultUri.Host}', but the registered KeyClient "
                    + $"reaches '{_client.VaultUri.Host}'."
            );
        }

        var result = await _client
            .GetCryptographyClient(identifier.Name, identifier.Version)
            .UnwrapKeyAsync(KeyWrapAlgorithm.RsaOaep256, wrapped.Ciphertext, cancellationToken)
            .ConfigureAwait(false);

        var payload = result.Key;
        Span<byte> expected = stackalloc byte[_ContextHashLength];

        try
        {
            SHA256.HashData(KeyWrapContextEncoding.Encode(ContextLabel, context), expected);

            if (
                payload.Length != _DataKeyLength + _ContextHashLength
                || !CryptographicOperations.FixedTimeEquals(payload.AsSpan(_DataKeyLength), expected)
            )
            {
                throw new CryptographicException(
                    "The unwrapped data key does not belong to this scope, id, and kind; the wrapped key was moved or "
                        + "changed."
                );
            }

            return payload[.._DataKeyLength];
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }
}
