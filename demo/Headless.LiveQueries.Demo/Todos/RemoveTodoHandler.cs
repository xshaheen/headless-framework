// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Headless.UnitOfWork;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Demo;

public sealed class RemoveTodoHandler(AppDbContext db, IUnitOfWorkFactory units)
    : IRequestHandler<RemoveTodo, ApiResult>
{
    public async ValueTask<ApiResult> Handle(RemoveTodo request, CancellationToken cancellationToken)
    {
        return await units.RunAsync(
            db,
            async (_, token) =>
            {
                // Read, then remove: the delete is matched on the owner the row was read with. ExecuteDelete
                // would announce the whole table.
                var todo = await db.Todos.SingleOrDefaultAsync(todo => todo.Id == request.Id, token);

                if (todo is null)
                {
                    return ApiResult.NotFound(nameof(Todo), request.Id);
                }

                db.Todos.Remove(todo);
                await db.SaveChangesAsync(token);

                return ApiResult.Ok();
            },
            cancellationToken
        );
    }
}
