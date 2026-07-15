// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Entities;
using Headless.Jobs.Instrumentation;
using Microsoft.Extensions.DependencyInjection;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Headless.Jobs;

/// <summary>
/// Entry point for registering OpenTelemetry instrumentation on the Jobs scheduler.
/// </summary>
public static class SetupOpenTelemetry
{
    /// <summary>
    /// Replaces the default <c>IJobsInstrumentation</c> logger-based implementation with an
    /// OpenTelemetry activity-tracing implementation, so job execution lifecycle events are emitted
    /// as <see cref="System.Diagnostics.Activity"/> spans instead of plain log entries.
    /// </summary>
    /// <remarks>
    /// Call this method after configuring an OpenTelemetry <c>TracerProvider</c> in the application.
    /// Each job execution creates a root activity whose display name is the job function name. The
    /// activity carries job metadata (id, type, retry count) as tags.
    /// </remarks>
    /// <param name="jobsConfiguration">The jobs options builder.</param>
    public static JobsOptionsBuilder<TTimeJob, TCronJob> AddOpenTelemetryInstrumentation<TTimeJob, TCronJob>(
        this JobsOptionsBuilder<TTimeJob, TCronJob> jobsConfiguration
    )
        where TTimeJob : TimeJobEntity<TTimeJob>, new()
        where TCronJob : CronJobEntity, new()
    {
        jobsConfiguration.ExternalProviderConfigServiceAction += services =>
        {
            // Replace any existing instrumentation with OpenTelemetry version
            services.AddSingleton<IJobsInstrumentation, OpenTelemetryInstrumentation>();
        };

        return jobsConfiguration;
    }
}
