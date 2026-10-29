// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Encryption;

/// <summary>One row of the data-key store: a wrapped data key and its identity.</summary>
[PublicAPI]
public sealed class StoredDataKey
{
    /// <summary>Gets the key's id, which every frame it encrypts names.</summary>
    public required Guid KeyId { get; init; }

    /// <summary>Gets the key's scope: a tenant id, <see cref="FieldContext.HostScope" />, or a per-subject scope.</summary>
    public required string Scope { get; init; }

    /// <summary>Gets what the key is for.</summary>
    public required DataKeyKind Kind { get; init; }

    /// <summary>Gets the key's version within its scope and kind, from 1. The highest version is active.</summary>
    public required int Version { get; init; }

    /// <summary>Gets the <see cref="IKeyEncryptionKeyProvider.Name" /> of the provider that wrapped the key.</summary>
    public required string KekProvider { get; init; }

    /// <summary>Gets the wrapped key.</summary>
    public required WrappedDataKey WrappedKey { get; init; }

    /// <summary>Gets when the key was stored, by the store's clock. Ignored on insert.</summary>
    public DateTimeOffset CreatedAt { get; init; }
}
