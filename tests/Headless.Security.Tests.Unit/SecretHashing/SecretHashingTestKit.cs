// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Security;
using Microsoft.Extensions.Options;

namespace Tests.SecretHashing;

internal static class SecretHashingTestKit
{
    // Low enough to keep the suite fast; production cost is covered by the options defaults and the startup check.
    public const int LowIterations = 1_000;

    public static SecretHasherOptions Pbkdf2Options(int iterations = LowIterations, int hashSize = 32)
    {
        return new SecretHasherOptions
        {
            Algorithm = SecretHashAlgorithms.Pbkdf2Sha256,
            Pbkdf2Sha256 = new Pbkdf2HashParameters
            {
                Iterations = iterations,
                SaltSize = 16,
                HashSize = hashSize,
            },
        };
    }

    public static SecretHasher CreateHasher(SecretHasherOptions options, params ISecretHashAlgorithm[] extra)
    {
        var wrapped = Options.Create(options);

        return new SecretHasher(wrapped, [new Pbkdf2Sha256SecretHashAlgorithm(wrapped), .. extra]);
    }
}

/// <summary>A deterministic algorithm that records every derivation it is asked to perform.</summary>
internal sealed class RecordingSecretHashAlgorithm(string id = "stub") : ISecretHashAlgorithm
{
    public List<int> DerivationLengths { get; } = [];

    public int HashCalls { get; private set; }

    public bool RequestRehash { get; set; }

    public string Id { get; } = id;

    public string Hash(ReadOnlySpan<byte> secret)
    {
        HashCalls++;
        Span<byte> hash = stackalloc byte[32];
        _Derive(secret, hash);

        return new PhcString(Id, null, [], new byte[16], hash).ToString();
    }

    public bool TryComputeHash(ReadOnlySpan<byte> secret, PhcString encoded, Span<byte> destination)
    {
        DerivationLengths.Add(destination.Length);
        _Derive(secret, destination);

        return true;
    }

    public bool NeedsRehash(PhcString encoded)
    {
        return RequestRehash;
    }

    private static void _Derive(ReadOnlySpan<byte> secret, Span<byte> destination)
    {
        Span<byte> digest = stackalloc byte[64];
        SHA512.HashData(secret, digest);
        digest[..destination.Length].CopyTo(destination);
    }
}
