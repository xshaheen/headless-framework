// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Domain;

/// <summary>Defines an entity. It's primary key may not be "ID" or it may have a composite primary key.</summary>
[PublicAPI]
public interface IEntity
{
    /// <summary>Returns an array of ordered keys for this entity.</summary>
    IReadOnlyList<object> GetKeys();

    /// <summary>Returns a colon-delimited composite key string built from <c>GetKeys()</c>.</summary>
    string GetKey() => string.Join(':', GetKeys());
}
