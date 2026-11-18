// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Mediator;

namespace Demo;

public sealed record GetCounter : IRequest<ApiResult<CounterView>>;
