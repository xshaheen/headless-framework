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
