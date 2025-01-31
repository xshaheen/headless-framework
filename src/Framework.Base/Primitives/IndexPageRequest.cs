// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

public interface IIndexPageRequest
{
    public int Index { get; }

    public int Size { get; }
}

public abstract class IndexPageRequest : IIndexPageRequest
{
    public required int Index { get; init; }

    public required int Size { get; init; }
}
