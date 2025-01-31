// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

public interface IHasOrderByRequest
{
    public OrderBy? Order { get; }
}

public interface IHasMultiOrderByRequest
{
    public List<OrderBy>? Orders { get; }
}
