// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Abstractions;

/// <summary>Exposes the entity tag for an HTTP response representation.</summary>
[PublicAPI]
public interface IHasEntityTag
{
    /// <summary>Gets the entity tag that identifies the current response representation.</summary>
    EntityTag EntityTag { get; }
}
