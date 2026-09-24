// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Headless.Checks;
using Microsoft.Extensions.Options;

namespace Headless.Security;

/// <summary>Hashes and verifies secrets by dispatching to the <see cref="ISecretHashAlgorithm" /> a hash names.</summary>
internal sealed class SecretHasher : ISecretHasher
{
    // Secrets up to this many UTF-8 bytes are encoded on the stack; longer ones use a pooled buffer.
    private const int _StackallocThreshold = 256;

    // Throwing on invalid input turns a lone surrogate into a rejection instead of a silent U+FFFD substitution,
    // which would make distinct invalid secrets hash identically.
    private static readonly UTF8Encoding _StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true
    );

    private readonly IOptions<SecretHasherOptions> _options;
    private readonly Dictionary<string, ISecretHashAlgorithm> _algorithms = new(StringComparer.Ordinal);

    public SecretHasher(IOptions<SecretHasherOptions> options, IEnumerable<ISecretHashAlgorithm> algorithms)
    {
        _options = options;

        foreach (var algorithm in algorithms)
        {
            // The first registration of an id wins, matching TryAdd semantics everywhere else in the registration.
            _algorithms.TryAdd(algorithm.Id, algorithm);
        }
    }

    public string Hash(ReadOnlySpan<char> secret)
    {
        var options = _options.Value;

        if (secret.IsEmpty)
        {
            throw new ArgumentException("The secret must not be empty.", nameof(secret));
        }

        if (secret.Length > options.MaxSecretLength)
        {
            throw new ArgumentException(
                $"The secret is longer than the configured maximum of {options.MaxSecretLength} characters.",
                nameof(secret)
            );
        }

        var algorithm = _GetConfiguredAlgorithm(options);

        if (!_TryWithUtf8(secret, bytes => algorithm.Hash(bytes), out var encoded))
        {
            throw new ArgumentException(
                "The secret is not valid UTF-16: it contains a lone surrogate.",
                nameof(secret)
            );
        }

        return encoded;
    }

    public SecretVerification Verify(ReadOnlySpan<char> secret, string encoded)
    {
        Argument.IsNotNull(encoded);

        var options = _options.Value;

        if (secret.IsEmpty || secret.Length > options.MaxSecretLength)
        {
            return SecretVerification.Failed;
        }

        if (!PhcString.TryParse(encoded, out var stored) || !_algorithms.TryGetValue(stored.Id, out var algorithm))
        {
            return SecretVerification.Failed;
        }

        return _TryWithUtf8(secret, bytes => _Verify(bytes, options, stored, algorithm), out var result)
            ? result
            : SecretVerification.Failed;
    }

    private SecretVerification _Verify(
        ReadOnlySpan<byte> secret,
        SecretHasherOptions options,
        PhcString stored,
        ISecretHashAlgorithm algorithm
    )
    {
        // The derivation always runs at the stored hash length, and the comparison always covers the whole buffer,
        // so the work done does not depend on the secret the caller supplied.
        var length = stored.Hash.Length;
        var rented = length > _StackallocThreshold ? ArrayPool<byte>.Shared.Rent(length) : null;
        var derived = rented is null ? stackalloc byte[length] : rented.AsSpan(0, length);

        try
        {
            if (
                !algorithm.TryComputeHash(secret, stored, derived)
                || !CryptographicOperations.FixedTimeEquals(derived, stored.Hash)
            )
            {
                return SecretVerification.Failed;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(derived);

            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        var upgrade =
            !string.Equals(stored.Id, options.Algorithm, StringComparison.Ordinal) || algorithm.NeedsRehash(stored);

        return new SecretVerification(true, upgrade ? _GetConfiguredAlgorithm(options).Hash(secret) : null);
    }

    private ISecretHashAlgorithm _GetConfiguredAlgorithm(SecretHasherOptions options)
    {
        if (_algorithms.TryGetValue(options.Algorithm, out var algorithm))
        {
            return algorithm;
        }

        throw new InvalidOperationException(SecretHasherErrors.AlgorithmNotRegistered(options.Algorithm));
    }

    private delegate TResult Utf8Callback<out TResult>(ReadOnlySpan<byte> bytes);

    /// <summary>
    /// Encodes <paramref name="secret" /> as strict UTF-8 into a buffer that is zeroed afterwards, then runs
    /// <paramref name="callback" /> over it. Returns <see langword="false" /> when the secret is not valid UTF-16.
    /// </summary>
    private static bool _TryWithUtf8<TResult>(
        ReadOnlySpan<char> secret,
        Utf8Callback<TResult> callback,
        [MaybeNullWhen(false)] out TResult result
    )
    {
        int byteCount;

        try
        {
            byteCount = _StrictUtf8.GetByteCount(secret);
        }
        catch (EncoderFallbackException)
        {
            result = default;

            return false;
        }

        var rented = byteCount > _StackallocThreshold ? ArrayPool<byte>.Shared.Rent(byteCount) : null;
        var buffer = rented is null ? stackalloc byte[byteCount] : rented.AsSpan(0, byteCount);

        try
        {
            _StrictUtf8.GetBytes(secret, buffer);
            result = callback(buffer);

            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);

            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }
}
