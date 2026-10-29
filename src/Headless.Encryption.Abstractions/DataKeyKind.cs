// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Encryption;

/// <summary>What a data key is for. The value is stored with the key, so members keep their numbers.</summary>
#pragma warning disable CA1008 // The value is stored with every key, and no key lacks a kind, so no member is zero.
[PublicAPI]
public enum DataKeyKind
{
    /// <summary>Encrypts field values through <see cref="IFieldEncryptor" />.</summary>
    Data = 1,

    /// <summary>Keys a blind index (a keyed hash of a value, for equality search) for the same scope.</summary>
    Lookup = 2,
}
#pragma warning restore CA1008
