// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

/// <summary>Base class for token-based (continuation/cursor) page requests.</summary>
[PublicAPI]
public abstract class ContinuationPageRequest : IContinuationPageRequest
{
    /// <inheritdoc/>
    public string? ContinuationToken { get; init; }

    /// <inheritdoc/>
    public int Size { get; init; }
}
