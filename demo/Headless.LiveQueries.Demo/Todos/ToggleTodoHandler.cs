// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Headless.UnitOfWork;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Demo;

public sealed class ToggleTodoHandler(AppDbContext db, IUnitOfWorkFactory units)
    : IRequestHandler<ToggleTodo, ApiResult<TodoView>>
{
    public async ValueTask<ApiResult<TodoView>> Handle(ToggleTodo request, CancellationToken cancellationToken)
    {
        return await units.RunAsync(
            db,
            async (_, token) =>
            {
                // Loaded with a tracking query, so the save knows the owner the row was stored with and wakes
                // only that owner's list. An ExecuteUpdate, or an entity attached by hand, would wake every list.
                var todo = await db.Todos.SingleOrDefaultAsync(todo => todo.Id == request.Id, token);

                if (todo is null)
                {
                    return ApiResult<TodoView>.NotFound(nameof(Todo), request.Id);
                }

                todo.Done = !todo.Done;
                await db.SaveChangesAsync(token);

                return ApiResult<TodoView>.Ok(new TodoView(todo.Id, todo.Title, todo.Done, todo.OwnerId));
            },
            cancellationToken
        );
    }
}
