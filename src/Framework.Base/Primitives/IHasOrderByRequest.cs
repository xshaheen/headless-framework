// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Primitives;

public interface IHasOrderByRequest
{
    OrderBy? Order { get; }
}

public interface IHasMultiOrderByRequest
{
    List<OrderBy>? Orders { get; }
}
