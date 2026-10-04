// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

namespace Headless.Api;

/// <summary>Configures entity-tag concurrency validation shared by MVC and Minimal APIs.</summary>
[PublicAPI]
public sealed class EntityTagConcurrencyOptions
{
    /// <summary>
    /// Gets or sets an optional validator for a parsed strong <c>If-Match</c> entity tag.
    /// </summary>
    /// <remarks>
    /// Use this when an API accepts a specific representation format, such as
    /// <see cref="EntityTag.FromUInt32(uint)"/>. Returning <see langword="false"/> produces the standard
    /// invalid <c>If-Match</c> response.
    /// </remarks>
    public Func<EntityTag, bool>? IfMatchValidator { get; set; }
}
