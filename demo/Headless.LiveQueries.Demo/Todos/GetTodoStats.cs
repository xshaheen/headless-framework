// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Mediator;

namespace Demo;

/// <summary>A query over every todo: it has no filter, so any write to the table runs it again.</summary>
public sealed record GetTodoStats : IRequest<ApiResult<TodoStats>>;
