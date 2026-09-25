// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Security;

/// <summary>
/// The bounds every configured value must satisfy, and every stored hash must satisfy before it is verified.
/// </summary>
/// <remarks>
/// A stored hash is untrusted input: whoever can write the column controls the cost parameters a verification runs
/// with. Verification rejects any encoding outside these bounds before deriving anything, so a planted value cannot
/// force an unbounded allocation or CPU burn. The lower bounds are the minimums the underlying primitives accept.
/// </remarks>
[PublicAPI]
public static class SecretHashLimits
{
    /// <summary>The minimum salt or hash length, in bytes.</summary>
    public const int MinSaltOrHashSize = 16;

    /// <summary>The maximum salt or hash length, in bytes.</summary>
    public const int MaxSaltOrHashSize = 64;

    /// <summary>The Argon2id salt length, in bytes. libsodium derives Argon2id with a 16-byte salt only.</summary>
    public const int Argon2idSaltSize = 16;

    /// <summary>The minimum Argon2id memory size, in KiB (libsodium's minimum).</summary>
    public const int MinArgon2idMemorySize = 8;

    /// <summary>The maximum Argon2id memory size, in KiB (256 MiB).</summary>
    public const int MaxArgon2idMemorySize = 262_144;

    /// <summary>The minimum Argon2id iteration count.</summary>
    public const int MinArgon2idIterations = 1;

    /// <summary>The maximum Argon2id iteration count.</summary>
    public const int MaxArgon2idIterations = 16;

    /// <summary>The minimum PBKDF2 iteration count.</summary>
    public const int MinPbkdf2Iterations = 1;

    /// <summary>The maximum PBKDF2 iteration count.</summary>
    public const int MaxPbkdf2Iterations = 10_000_000;

    /// <summary>The maximum configurable secret length, in UTF-16 code units.</summary>
    public const int MaxSecretLength = 16_384;
}
