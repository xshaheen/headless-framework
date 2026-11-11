// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Mediator;

namespace Demo;

/// <summary>A command: every effect is in the database transaction.</summary>
public sealed record AddTodo(string Title, int OwnerId = 1) : IRequest<ApiResult<TodoView>>;
