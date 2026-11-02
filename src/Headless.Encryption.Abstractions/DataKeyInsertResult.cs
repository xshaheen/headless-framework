// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Encryption;

/// <summary>The outcome of <see cref="IDataKeyStore.TryInsertAsync" />.</summary>
#pragma warning disable CA1008 // Every insert has an outcome, so a zero member would name none.
[PublicAPI]
public enum DataKeyInsertResult
{
    /// <summary>The key was inserted.</summary>
    Inserted = 1,

    /// <summary>The scope already has a key of the same kind and version; nothing was written.</summary>
    VersionTaken = 2,

    /// <summary>The scope was shredded and the insert did not ask to reopen it; nothing was written.</summary>
    ScopeShredded = 3,
}
#pragma warning restore CA1008
