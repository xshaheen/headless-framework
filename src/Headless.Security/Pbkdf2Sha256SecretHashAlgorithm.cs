// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Headless.Security;

/// <summary>
/// PBKDF2 with HMAC-SHA256, encoded as <c>$pbkdf2-sha256$i=&lt;iterations&gt;,l=&lt;hash length&gt;$&lt;salt&gt;$&lt;hash&gt;</c>.
/// </summary>
/// <remarks>
/// PHC defines no PBKDF2 identifier; this follows the RustCrypto/Auth0 convention, which is PHC-conformant (the passlib
/// form uses a bare rounds field and a different base64 alphabet).
/// </remarks>
internal sealed class Pbkdf2Sha256SecretHashAlgorithm(IOptions<SecretHasherOptions> options) : ISecretHashAlgorithm
{
    // Shared by the writer and the parser so the two cannot drift apart.
    private const string _IterationsName = "i";
    private const string _LengthName = "l";

    public string Id => SecretHashAlgorithms.Pbkdf2Sha256;

    public string Hash(ReadOnlySpan<byte> secret)
    {
        var parameters = options.Value.Pbkdf2Sha256;
        Span<byte> salt = stackalloc byte[parameters.SaltSize];
        Span<byte> hash = stackalloc byte[parameters.HashSize];
        RandomNumberGenerator.Fill(salt);
        Rfc2898DeriveBytes.Pbkdf2(secret, salt, hash, parameters.Iterations, HashAlgorithmName.SHA256);

        var encoded = new PhcString(
            Id,
            version: null,
            [
                new PhcParameter(_IterationsName, parameters.Iterations.ToString(CultureInfo.InvariantCulture)),
                new PhcParameter(_LengthName, parameters.HashSize.ToString(CultureInfo.InvariantCulture)),
            ],
            salt,
            hash
        );

        CryptographicOperations.ZeroMemory(hash);

        return encoded.ToString();
    }

    public bool TryComputeHash(ReadOnlySpan<byte> secret, PhcString encoded, Span<byte> destination)
    {
        if (!_TryReadIterations(encoded, out var iterations) || destination.Length != encoded.Hash.Length)
        {
            return false;
        }

        Rfc2898DeriveBytes.Pbkdf2(secret, encoded.Salt, destination, iterations, HashAlgorithmName.SHA256);

        return true;
    }

    public bool NeedsRehash(PhcString encoded)
    {
        var configured = options.Value.Pbkdf2Sha256;

        return !_TryReadIterations(encoded, out var iterations)
            || iterations < configured.Iterations
            || encoded.Salt.Length < configured.SaltSize
            || encoded.Hash.Length < configured.HashSize;
    }

    private static bool _TryReadIterations(PhcString encoded, out int iterations)
    {
        iterations = 0;

        // Exactly "i=<n>,l=<n>" in that order: one accepted spelling per hash, and no unknown parameter slips past.
        return encoded.Version is null
            && encoded.Parameters is [{ Name: _IterationsName }, { Name: _LengthName }]
            && encoded.TryGetInt32(_IterationsName, out iterations)
            && encoded.TryGetInt32(_LengthName, out var length)
            && iterations is >= SecretHashLimits.MinPbkdf2Iterations and <= SecretHashLimits.MaxPbkdf2Iterations
            && length == encoded.Hash.Length
            && _IsSizeInBounds(encoded.Salt.Length)
            && _IsSizeInBounds(encoded.Hash.Length);
    }

    private static bool _IsSizeInBounds(int size)
    {
        return size is >= SecretHashLimits.MinSaltOrHashSize and <= SecretHashLimits.MaxSaltOrHashSize;
    }
}
