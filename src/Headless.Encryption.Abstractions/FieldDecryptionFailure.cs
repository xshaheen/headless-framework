// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Encryption;

/// <summary>Why <see cref="IFieldEncryptor.DecryptAsync" /> refused a value.</summary>
#pragma warning disable CA1008 // Every refusal has a cause, so a zero member would name none.
[PublicAPI]
public enum FieldDecryptionFailure
{
    /// <summary>
    /// The frame's data key does not exist: its scope was shredded, or the frame names a key from another key store.
    /// </summary>
    KeyNotFound = 1,

    /// <summary>
    /// The authentication tag did not verify: the purpose, scope, or binding differs from encryption, or the frame
    /// was changed.
    /// </summary>
    AuthenticationFailed = 2,

    /// <summary>The frame's algorithm is unknown to this release.</summary>
    UnsupportedAlgorithm = 3,
}
#pragma warning restore CA1008
