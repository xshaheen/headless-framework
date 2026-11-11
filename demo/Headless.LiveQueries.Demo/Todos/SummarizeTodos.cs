// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Mediator;

namespace Demo;

/// <summary>An action: it reads, calls something outside the database, and writes nothing.</summary>
public sealed record SummarizeTodos : IRequest<ApiResult<TodoSummary>>;
