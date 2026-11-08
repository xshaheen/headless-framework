// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Headless.Jobs;

/// <summary>
/// Teaches a Jobs model to judge lease expiry by the database clock, so a query that filters on a live lease
/// (<c>WhereLeaseLive</c>, <c>WhereLeaseGrantable</c>) asks the database what time it is instead of trusting the
/// node's clock. Every path that builds a Jobs model runs it, which is also where an unsupported provider fails.
/// </summary>
internal static class JobsLeaseModelConfiguration
{
    internal static void Configure(ModelBuilder builder, DbContext context)
    {
        builder.UseFencingDatabaseClock(JobsSqlDialects.Resolve(context));
    }
}
