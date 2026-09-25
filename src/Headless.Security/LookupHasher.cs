// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;

namespace Headless.Security;

/// <summary>Creates deterministic, salted lookup hashes using configured PBKDF2 options.</summary>
internal sealed class LookupHasher(LookupHasherOptions options) : ILookupHasher
{
    /// <inheritdoc />
    public string Create(string value, string? salt = null)
    {
        salt ??= options.DefaultSalt ?? string.Empty;

        var bytes = Rfc2898DeriveBytes.Pbkdf2(
            value,
            Encoding.UTF8.GetBytes(salt),
            options.Iterations,
            options.Algorithm,
            options.SizeInBytes
        );

        return Convert.ToBase64String(bytes);
    }
}
