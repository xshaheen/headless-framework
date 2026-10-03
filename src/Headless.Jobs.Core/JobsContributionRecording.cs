// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

internal static class JobsContributionRecording
{
    public static void AddJobsModuleContribution<TModule>(this IServiceCollection services)
        where TModule : IJobsModule
    {
        services.AddSingleton(new JobsModuleContribution(typeof(TModule), static catalog => TModule.Register(catalog)));
    }

    public static void AddJobTuning(
        this IServiceCollection services,
        string identity,
        Action<JobTuningBuilder> configure
    )
    {
        Argument.IsNotNullOrWhiteSpace(identity);
        Argument.IsNotNull(configure);

        var builder = new JobTuningBuilder(identity);
        configure(builder);
        services.AddSingleton(new JobsTuningContribution(builder.Build()));
    }
}
