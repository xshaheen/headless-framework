// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>
/// A zero-based offset (index/size) page request. The type is concrete so a JSON body or a query string binds it
/// directly, for example <c>{"page":{"index":1,"size":5}}</c> or <c>?page.index=1&amp;page.size=5</c>.
/// </summary>
[PublicAPI]
public sealed class IndexPageRequest : IIndexPageRequest
{
    /// <inheritdoc/>
    public required int Index { get; init; }

    /// <inheritdoc/>
    public required int Size { get; init; }
}

/// <summary>A request for a page using zero-based offset (index/size) pagination.</summary>
[PublicAPI]
public interface IIndexPageRequest
{
    /// <summary>The zero-based index of the requested page.</summary>
    int Index { get; }

    /// <summary>The requested page size.</summary>
    int Size { get; }
}
