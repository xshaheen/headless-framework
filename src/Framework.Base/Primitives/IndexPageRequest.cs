// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

public interface IIndexPageRequest
{
    int Index { get; }

    int Size { get; }
}

public abstract class IndexPageRequest : IIndexPageRequest
{
    public required int Index { get; init; }

    public required int Size { get; init; }
}
