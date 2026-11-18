// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Mediator;

namespace Demo;

/// <summary>A query: one owner's todos. With row-level matching, another owner's write does not run it again.</summary>
public sealed record ListOwnerTodos(int OwnerId) : IRequest<ApiResult<IReadOnlyList<TodoView>>>;
