// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Primitives;

public interface IHasOrderByRequest
{
    OrderBy? Order { get; }
}

public interface IHasMultiOrderByRequest
{
    List<OrderBy>? Orders { get; }
}
