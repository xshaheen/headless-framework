// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Data;
using Headless.Primitives;
using Headless.UnitOfWork;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace Demo;

public sealed class IncrementCounterHandler(AppDbContext db, IUnitOfWorkFactory units)
    : IRequestHandler<IncrementCounter, ApiResult<CounterView>>
{
    private const int _MaxHoldMilliseconds = 2000;

    // Every writer that loses the version check means another one committed, so the cap is set above the
    // number of requests the page and the scripts send at once.
    private const int _MaxAttempts = 20;

    public async ValueTask<ApiResult<CounterView>> Handle(IncrementCounter request, CancellationToken cancellationToken)
    {
        var hold = TimeSpan.FromMilliseconds(Math.Clamp(request.HoldMilliseconds, 0, _MaxHoldMilliseconds));

        return await units.RunAsync(
            db,
            async (_, token) =>
            {
                // The read is inside the block, so a replay reads the row again on an empty change tracker.
                var counter = await db.Counters.SingleAsync(token);

                await Task.Delay(hold, token);

                counter.Value++;
                counter.Version++;

                // Throws DbUpdateConcurrencyException when another request saved since the read above.
                await db.SaveChangesAsync(token);

                return ApiResult<CounterView>.Ok(new CounterView(counter.Value));
            },
            // SQLite takes its write lock when an ordinary transaction begins, which would run these blocks one
            // after another and never lose a check. Read-uncommitted is the SQLite mode that reads without the
            // lock, the way PostgreSQL and SQL Server read at their default level.
            IsolationLevel.ReadUncommitted,
            // The opt-in. Without a policy the losing save reaches the caller, and the host answers 409.
            retry: request.Retry ? EntityFrameworkConflictRetry.Create(_MaxAttempts) : null,
            cancellationToken
        );
    }
}
