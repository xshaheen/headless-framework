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
    private const int _StackBufferLength = 256;

    // Labels the hash key apart from the cipher's subkeys. They derive from different keys already; the label keeps the
    // two uses distinct even if a key were ever shared.
    // The lenient encoder turns every lone surrogate into U+FFFD, so two different values would share one index and a
    // lookup for one would match the other's row.
    private static readonly UTF8Encoding _StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

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

    public async ValueTask<IReadOnlyList<BlindIndex>> ComputeAllAsync(
        BlindIndexContext context,
        IReadOnlyList<string> values,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);
        _EnsureHashable(values);

        if (values.Count == 0)
        {
            return [];
        }

        var scope = _ResolveScope(context);

        using var key = await ring.GetOrCreateActiveAsync(
                scope,
                DataKeyKind.Lookup,
                reopenShreddedScope: false,
                cancellationToken
            )
            .ConfigureAwait(false);

        var indexes = new BlindIndex[values.Count];

        for (var i = 0; i < values.Count; i++)
        {
            indexes[i] = new BlindIndex(key.Version, _Hash(key.Key, context, values[i]));
        }

        return indexes;
    }

    public async ValueTask<IReadOnlyList<IReadOnlyList<BlindIndex>>> ComputeAllCandidatesAsync(
        BlindIndexContext context,
        IReadOnlyList<string> values,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(context);
        _EnsureHashable(values);

        if (values.Count == 0)
        {
            return [];
        }

        var scope = _ResolveScope(context);

        var keys = await ring.LeaseVersionsAsync(scope, DataKeyKind.Lookup, cancellationToken).ConfigureAwait(false);

        try
        {
            var candidates = new IReadOnlyList<BlindIndex>[values.Count];

            for (var i = 0; i < values.Count; i++)
            {
                var hashes = new BlindIndex[keys.Count];

                for (var k = 0; k < keys.Count; k++)
                {
                    hashes[k] = new BlindIndex(keys[k].Version, _Hash(keys[k].Key, context, values[i]));
                }

                candidates[i] = hashes;
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

    // Checks every value before a key is read or created, so a bad value late in a batch leaves no key behind and no
    // partial result. The message names the position, never the value.
    private static void _EnsureHashable(IReadOnlyList<string> values)
    {
        Argument.HasNoNulls(values);

        for (var i = 0; i < values.Count; i++)
        {
            try
            {
                _StrictUtf8.GetByteCount(values[i]);
            }
            catch (EncoderFallbackException e)
            {
                throw new ArgumentException(
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"The value at index {i} is not valid UTF-16: it contains a lone surrogate."
                    ),
                    nameof(values),
                    e
                );
            }
        }
    }

    private static byte[] _Hash(ReadOnlySpan<byte> lookupKey, BlindIndexContext context, string value)
    {
        // The purpose and a typical looked-up value are short, so both fit on the stack; a longer one uses the heap.
        var infoLength = SubkeyInfoPrefix.Length + Encoding.UTF8.GetByteCount(context.Purpose);
        var info = infoLength <= _StackBufferLength ? stackalloc byte[_StackBufferLength] : new byte[infoLength];
        info = info[..infoLength];
        SubkeyInfoPrefix.CopyTo(info);
        Encoding.UTF8.GetBytes(context.Purpose, info[SubkeyInfoPrefix.Length..]);

        int inputLength;

        try
        {
            inputLength = _StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException e)
        {
            throw new ArgumentException(
                "The value is not valid UTF-16: it contains a lone surrogate.",
                nameof(value),
                e
            );
        }

        var input = inputLength <= _StackBufferLength ? stackalloc byte[_StackBufferLength] : new byte[inputLength];
        input = input[..inputLength];
        _StrictUtf8.GetBytes(value, input);
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
            "Computing the blind index",
            context.Purpose,
            "BlindIndexContext.Host(purpose)",
            nameof(BlindIndexContext.Scope)
        );
}
