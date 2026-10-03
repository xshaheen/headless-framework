// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.InteropServices;
using Headless.Jobs.Enums;
using Headless.Jobs.Models;

namespace Headless.Jobs;

internal interface IJobsOptionsSeeding
{
    bool SeedDefinedCronJobs { get; }
    Func<IServiceProvider, Task>? TimeSeederAction { get; }
    Func<IServiceProvider, Task>? CronSeederAction { get; }
}
