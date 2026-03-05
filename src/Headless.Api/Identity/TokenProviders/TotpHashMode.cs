// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Api.Identity.TokenProviders;

/// <summary>
/// Specifies the HMAC hash algorithm used for TOTP code generation and validation.
/// </summary>
public enum TotpHashMode
{
    /// <summary>HMAC-SHA1 (20-byte hash). Default per RFC 6238.</summary>
    Sha1 = 0,

    /// <summary>HMAC-SHA256 (32-byte hash).</summary>
    Sha256 = 1,

    /// <summary>HMAC-SHA512 (64-byte hash).</summary>
    Sha512 = 2,
}
