// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Mediator;

namespace Demo;

public sealed record ToggleTodo(Guid Id) : IRequest<ApiResult<TodoView>>;
