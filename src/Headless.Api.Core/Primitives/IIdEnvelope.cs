// Copyright (c) Mahmoud Shaheen. All rights reserved.

#pragma warning disable CA2225 // Named From* methods would only duplicate the public constructor the implicit conversion already calls.
namespace Headless.Primitives;

/// <summary>Marker contract for response envelopes that carry a single string identifier.</summary>
public interface IIdEnvelope
{
    /// <summary>The string representation of the resource identifier.</summary>
    string Id { get; }
}
