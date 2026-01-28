// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

public interface IContinuationPageRequest
{
    string? ContinuationToken { get; init; }

    int Size { get; init; }
}

public abstract class ContinuationPageRequest : IContinuationPageRequest
{
    public string? ContinuationToken { get; init; }

    public int Size { get; init; }
}
