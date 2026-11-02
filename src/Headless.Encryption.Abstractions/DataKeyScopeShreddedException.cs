// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Encryption;

/// <summary>
/// Thrown when a key is needed for a scope that was shredded. A shred is final: only
/// <see cref="IDataKeyManager.EnsureAsync" /> reopens the scope, with a new key.
/// </summary>
/// <param name="scope">The shredded scope.</param>
[PublicAPI]
public sealed class DataKeyScopeShreddedException(string scope)
    : InvalidOperationException(
        $"Scope '{scope}' was shredded, so no key is created for it implicitly. Call IDataKeyManager.EnsureAsync to "
            + "reopen it with a new key."
    )
{
    /// <summary>Gets the shredded scope.</summary>
    public string Scope { get; } = scope;
}
