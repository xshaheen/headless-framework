// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using NSec.Cryptography;

namespace Headless.Security;

/// <summary>
/// Argon2id (RFC 9106) through libsodium, encoded as
/// <c>$argon2id$v=19$m=&lt;KiB&gt;,t=&lt;iterations&gt;,p=1$&lt;salt&gt;$&lt;hash&gt;</c>.
/// </summary>
/// <remarks>
/// libsodium derives Argon2id with one lane and a 16-byte salt only, so encodings with <c>p≠1</c> or another salt
/// length — for example hashes produced by another library with <c>p=4</c> — are refused rather than verified.
/// </remarks>
internal sealed class Argon2idSecretHashAlgorithm(IOptions<Argon2idHashOptions> options) : ISecretHashAlgorithm
{
    // The only version libsodium implements (0x13).
    private const int _Version = 19;

    // Shared by the writer and the parser so the two cannot drift apart.
    private const string _MemorySizeName = "m";
    private const string _IterationsName = "t";
    private const string _ParallelismName = "p";

    public string Id => SecretHashAlgorithms.Argon2id;

    public string Hash(ReadOnlySpan<byte> secret)
    {
        var parameters = options.Value;
        Span<byte> salt = stackalloc byte[SecretHashLimits.Argon2idSaltSize];
        Span<byte> hash = stackalloc byte[parameters.HashSize];
        RandomNumberGenerator.Fill(salt);
        Derive(secret, salt, parameters.MemorySize, parameters.Iterations, hash);

        var encoded = new PhcString(
            Id,
            _Version,
            [
                new PhcParameter(_MemorySizeName, parameters.MemorySize.ToString(CultureInfo.InvariantCulture)),
                new PhcParameter(_IterationsName, parameters.Iterations.ToString(CultureInfo.InvariantCulture)),
                new PhcParameter(_ParallelismName, "1"),
            ],
            salt,
            hash
        );

        CryptographicOperations.ZeroMemory(hash);

        return encoded.ToString();
    }

    public bool TryComputeHash(ReadOnlySpan<byte> secret, PhcString encoded, Span<byte> destination)
    {
        if (
            !_TryReadParameters(encoded, out var memorySize, out var iterations)
            || destination.Length != encoded.Hash.Length
        )
        {
            return false;
        }

        try
        {
            Derive(secret, encoded.Salt, memorySize, iterations, destination);

            return true;
        }
        catch (CryptographicException)
        {
            // libsodium reports failure (typically it could not allocate the requested memory) as a
            // CryptographicException. A stored hash must never turn verification into a throw, so this is a failed
            // verification rather than an error surfaced to the caller.
            return false;
        }
    }

    public bool NeedsRehash(PhcString encoded)
    {
        var configured = options.Value;

        return !_TryReadParameters(encoded, out var memorySize, out var iterations)
            || memorySize < configured.MemorySize
            || iterations < configured.Iterations
            || encoded.Hash.Length < configured.HashSize;
    }

    /// <summary>Derives Argon2id (one lane) into <paramref name="destination" />.</summary>
    internal static void Derive(
        ReadOnlySpan<byte> secret,
        ReadOnlySpan<byte> salt,
        int memorySize,
        int iterations,
        Span<byte> destination
    )
    {
        var algorithm = PasswordBasedKeyDerivationAlgorithm.Argon2id(
            new Argon2Parameters
            {
                DegreeOfParallelism = 1,
                MemorySize = memorySize,
                NumberOfPasses = iterations,
            }
        );

        algorithm.DeriveBytes(secret, salt, destination);
    }

    private static bool _TryReadParameters(PhcString encoded, out int memorySize, out int iterations)
    {
        memorySize = 0;
        iterations = 0;

        // Exactly "v=19$m=<n>,t=<n>,p=1" in that order. Every bound is checked here, before NSec sees the values: NSec
        // throws ArgumentException for parameters below libsodium's minimums, and a stored hash must not reach that.
        return encoded.Version == _Version
            && encoded.Parameters
                is [{ Name: _MemorySizeName }, { Name: _IterationsName }, { Name: _ParallelismName, Value: "1" }]
            && encoded.TryGetInt32(_MemorySizeName, out memorySize)
            && encoded.TryGetInt32(_IterationsName, out iterations)
            && memorySize is >= SecretHashLimits.MinArgon2idMemorySize and <= SecretHashLimits.MaxArgon2idMemorySize
            && iterations is >= SecretHashLimits.MinArgon2idIterations and <= SecretHashLimits.MaxArgon2idIterations
            && encoded.Salt.Length == SecretHashLimits.Argon2idSaltSize
            && encoded.Hash.Length is >= SecretHashLimits.MinSaltOrHashSize and <= SecretHashLimits.MaxSaltOrHashSize;
    }
}
