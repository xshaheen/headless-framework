// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Encryption;

/// <summary>
/// Identifies the data key a key-encryption-key provider wraps or unwraps. A provider that supports it binds the
/// wrapped key to this context (authenticated data, a KMS encryption context), so a wrapped key copied to another row
/// fails to unwrap.
/// </summary>
[PublicAPI]
public sealed class KeyWrapContext
{
    /// <summary>Gets the data key's scope.</summary>
    public required string Scope { get; init; }

    /// <summary>Gets the data key's id.</summary>
    public required Guid KeyId { get; init; }

    /// <summary>Gets the data key's kind.</summary>
    public required DataKeyKind Kind { get; init; }
}
