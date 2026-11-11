// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Demo;

// A query handler holds no live-query code. It reads through EF Core; the tracked context records the table and
// the owner filter it read through, and a committed write that can match them runs the handler again.
public sealed class ListOwnerTodosHandler(AppDbContext db)
    : IRequestHandler<ListOwnerTodos, ApiResult<IReadOnlyList<TodoView>>>
{
    public async ValueTask<ApiResult<IReadOnlyList<TodoView>>> Handle(
        ListOwnerTodos request,
        CancellationToken cancellationToken
    )
    {
        // Version 7 ids sort by creation time, which SQLite cannot do on a DateTimeOffset column.
        var todos = await db
            .Todos.Where(todo => todo.OwnerId == request.OwnerId)
            .OrderBy(todo => todo.Id)
            .Select(todo => new TodoView(todo.Id, todo.Title, todo.Done, todo.OwnerId))
            .ToListAsync(cancellationToken);

        return todos;
    }
}
