// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Coordination;
using Headless.Dashboard.Authentication;
using Headless.Jobs.Coordination;
using Headless.Jobs.Entities;
using Headless.Jobs.Hubs;
using Headless.Jobs.Infrastructure.Dashboard;
using Headless.Jobs.Interfaces;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Headless.Jobs;

internal sealed class JobsDashboardStartupFilter<TTimeJob, TCronJob>(DashboardOptionsBuilder config) : IStartupFilter
    where TTimeJob : TimeJobEntity<TTimeJob>, new()
    where TCronJob : CronJobEntity, new()
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            next(app);
            app.UseDashboardWithEndpoints<TTimeJob, TCronJob>(config);
        };
    }
}
