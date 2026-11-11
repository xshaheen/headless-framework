// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Demo;

public sealed class SummarizeTodosHandler(AppDbContext db) : IRequestHandler<SummarizeTodos, ApiResult<TodoSummary>>
{
    private static readonly TimeSpan _OutsideCall = TimeSpan.FromMilliseconds(600);

    public async ValueTask<ApiResult<TodoSummary>> Handle(SummarizeTodos request, CancellationToken cancellationToken)
    {
        var titles = await db.Todos.Where(todo => !todo.Done).Select(todo => todo.Title).ToListAsync(cancellationToken);

        // Stands in for a model or HTTP call: slow and outside any transaction. It commits nothing, so the
        // response tells a client that no stream will report this command.
        await Task.Delay(_OutsideCall, cancellationToken);

        return new TodoSummary(
            titles.Count == 0 ? "Nothing left to do." : $"{titles.Count} open: {string.Join(", ", titles)}."
        );
    }
}
