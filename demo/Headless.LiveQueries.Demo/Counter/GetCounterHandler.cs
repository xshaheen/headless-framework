// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Primitives;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Demo;

public sealed class GetCounterHandler(AppDbContext db) : IRequestHandler<GetCounter, ApiResult<CounterView>>
{
    public async ValueTask<ApiResult<CounterView>> Handle(GetCounter request, CancellationToken cancellationToken)
    {
        var counter = await db.Counters.AsNoTracking().SingleAsync(cancellationToken);

        return new CounterView(counter.Value);
    }
}
