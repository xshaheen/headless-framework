// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>Defines an opaque concurrency stamp used for optimistic concurrency checks.</summary>
/// <remarks>
/// The stamp is typically a random token (for example a GUID) refreshed on every write. Persistence layers
/// compare the stored stamp against the incoming value before committing and reject writes where the values differ.
/// </remarks>
[PublicAPI]
public interface IHasConcurrencyStamp
{
    /// <summary>Gets an opaque token that changes on every write for optimistic concurrency checks.</summary>
    string? ConcurrencyStamp { get; }
}
