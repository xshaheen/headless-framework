// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Demo;

public sealed class GetTodoStatsHandler(AppDbContext db) : IRequestHandler<GetTodoStats, ApiResult<TodoStats>>
{
    public async ValueTask<ApiResult<TodoStats>> Handle(GetTodoStats request, CancellationToken cancellationToken)
    {
        var total = await db.Todos.CountAsync(cancellationToken);
        var done = await db.Todos.CountAsync(todo => todo.Done, cancellationToken);

        return new TodoStats(total, done);
    }
}
