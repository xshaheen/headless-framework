// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>A request for a page using zero-based offset (index/size) pagination.</summary>
[PublicAPI]
public interface IIndexPageRequest
{
    /// <summary>The zero-based index of the requested page.</summary>
    int Index { get; }

    /// <summary>The requested page size.</summary>
    int Size { get; }
}

/// <summary>Base class for zero-based offset (index/size) page requests.</summary>
[PublicAPI]
public abstract class IndexPageRequest : IIndexPageRequest
{
    /// <inheritdoc/>
    public required int Index { get; init; }

    /// <inheritdoc/>
    public required int Size { get; init; }
}
