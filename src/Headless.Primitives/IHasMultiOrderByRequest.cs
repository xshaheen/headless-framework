// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>A request that carries an optional ordered list of sort instructions.</summary>
[PublicAPI]
public interface IHasMultiOrderByRequest
{
    /// <summary>The sort instructions to apply in order, or <see langword="null"/> for the default ordering.</summary>
    List<OrderBy>? Orders { get; }
}
