// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>A request for a page using token-based (continuation/cursor) pagination.</summary>
[PublicAPI]
public interface IContinuationPageRequest
{
    /// <summary>The token identifying where to resume, or <see langword="null"/> to start from the first page.</summary>
    string? ContinuationToken { get; init; }

    /// <summary>The requested page size.</summary>
    int Size { get; init; }
}

/// <summary>Base class for token-based (continuation/cursor) page requests.</summary>
[PublicAPI]
public abstract class ContinuationPageRequest : IContinuationPageRequest
{
    /// <inheritdoc/>
    public string? ContinuationToken { get; init; }

    /// <inheritdoc/>
    public int Size { get; init; }
}
