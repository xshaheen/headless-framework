// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>Base class for zero-based offset (index/size) page requests.</summary>
[PublicAPI]
public abstract class IndexPageRequest : IIndexPageRequest
{
    /// <inheritdoc/>
    public required int Index { get; init; }

    /// <inheritdoc/>
    public required int Size { get; init; }
}
