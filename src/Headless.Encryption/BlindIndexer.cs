// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using Headless.Checks;
using Headless.MultiTenancy;

namespace Headless.Encryption;

/// <summary>
/// HMAC-SHA256 under an HKDF-SHA256 subkey of the scope's lookup key, with the purpose as the subkey's info, truncated
/// to the context's length.
/// </summary>
internal sealed class BlindIndexer(DataKeyRing ring, ICurrentTenant currentTenant) : IBlindIndexer
{
    // Labels the hash key apart from the cipher's subkeys. They derive from different keys already; the label keeps the
    // two uses distinct even if a key were ever shared.
    private static ReadOnlySpan<byte> SubkeyInfoPrefix => "headless/blind-index/v1\0"u8;

    public async ValueTask<BlindIndex> ComputeAsync(
        BlindIndexContext context,
        string value,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(value);
        var scope = _ResolveScope(context);

        using var key = await ring.GetOrCreateActiveAsync(
                scope,
                DataKeyKind.Lookup,
                reopenShreddedScope: false,
                cancellationToken
            )
            .ConfigureAwait(false);

        return new BlindIndex(key.Version, _Hash(key.Key, context, value));
    }

    public async ValueTask<BlindIndex?> RehashAsync(
        BlindIndexContext context,
        string value,
        int storedKeyVersion,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(value);
        var scope = _ResolveScope(context);

        using var key = await ring.GetOrCreateActiveAsync(
                scope,
                DataKeyKind.Lookup,
                reopenShreddedScope: false,
                cancellationToken
            )
            .ConfigureAwait(false);

        return storedKeyVersion >= key.Version ? null : new BlindIndex(key.Version, _Hash(key.Key, context, value));
    }

    public async ValueTask<IReadOnlyList<BlindIndex>> ComputeCandidatesAsync(
        BlindIndexContext context,
        string value,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);
        Argument.IsNotNull(value);
        var scope = _ResolveScope(context);

        var keys = await ring.LeaseVersionsAsync(scope, DataKeyKind.Lookup, cancellationToken).ConfigureAwait(false);

        try
        {
            var candidates = new BlindIndex[keys.Count];

            for (var i = 0; i < keys.Count; i++)
            {
                candidates[i] = new BlindIndex(keys[i].Version, _Hash(keys[i].Key, context, value));
            }

            return candidates;
        }
        finally
        {
            foreach (var key in keys)
            {
                key.Dispose();
            }
        }
    }

    private static byte[] _Hash(ReadOnlySpan<byte> lookupKey, BlindIndexContext context, string value)
    {
        var info = new byte[SubkeyInfoPrefix.Length + Encoding.UTF8.GetByteCount(context.Purpose)];
        SubkeyInfoPrefix.CopyTo(info);
        Encoding.UTF8.GetBytes(context.Purpose, info.AsSpan(SubkeyInfoPrefix.Length));

        var input = Encoding.UTF8.GetBytes(value);
        Span<byte> hashKey = stackalloc byte[HMACSHA256.HashSizeInBytes];
        Span<byte> hash = stackalloc byte[HMACSHA256.HashSizeInBytes];

        try
        {
            HKDF.DeriveKey(HashAlgorithmName.SHA256, lookupKey, hashKey, salt: [], info);
            HMACSHA256.HashData(hashKey, input, hash);

            return hash[..context.Length].ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hashKey);
            CryptographicOperations.ZeroMemory(input);
        }
    }

    private string _ResolveScope(BlindIndexContext context) =>
        DataKeyScope.Resolve(
            context.Scope,
            currentTenant,
            $"Computing the blind index '{context.Purpose}'",
            "BlindIndexContext.Host(purpose)",
            nameof(BlindIndexContext.Scope)
        );
}
