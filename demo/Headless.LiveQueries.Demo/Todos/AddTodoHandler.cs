// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Headless.UnitOfWork;
using Mediator;

namespace Demo;

// A command handler opens its unit of work on the line it chooses; nothing opens one for it. The write is
// announced to live queries when the unit commits, and never when it rolls back.
public sealed class AddTodoHandler(AppDbContext db, IUnitOfWorkFactory units)
    : IRequestHandler<AddTodo, ApiResult<TodoView>>
{
    public async ValueTask<ApiResult<TodoView>> Handle(AddTodo request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            return ApiResult<TodoView>.ValidationFailed(("title", "A todo needs a title."));
        }

        return await units.RunAsync(
            db,
            async (_, token) =>
            {
                var todo = new Todo
                {
                    Id = Guid.CreateVersion7(),
                    Title = request.Title.Trim(),
                    OwnerId = request.OwnerId,
                };

                db.Todos.Add(todo);
                await db.SaveChangesAsync(token);

                return ApiResult<TodoView>.Ok(new TodoView(todo.Id, todo.Title, todo.Done, todo.OwnerId));
            },
            cancellationToken
        );
    }
}
