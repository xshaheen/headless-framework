// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Jobs;

/// <summary>Offers fluent options callbacks for ordinary one-shot scheduling.</summary>
/// <remarks>
/// Each callback runs synchronously once on a fresh builder before delegating to the matching options overload.
/// Async-void callbacks are unsupported. The scheduler retains ownership of validation, policy, and time handling.
/// </remarks>
[PublicAPI]
public static class JobSchedulerExtensions
{
    extension(IJobScheduler scheduler)
    {
        /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
        [Obsolete(
            "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
            error: true
        )]
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public Task<Guid> ScheduleAsync<TArgs>(
            TArgs request,
            DateTime executionTime,
            Action<JobOptionsBuilder> configure,
            CancellationToken cancellationToken = default
        ) =>
            throw new NotSupportedException(
                "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset."
            );

        /// <summary>Rejects implicit DateTime conversion; supply an explicit DateTimeOffset instant.</summary>
        [Obsolete(
            "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset.",
            error: true
        )]
        [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
        public Task<Guid> ScheduleAsync<TJob>(
            DateTime executionTime,
            Action<JobOptionsBuilder> configure,
            CancellationToken cancellationToken = default
        )
            where TJob : IJob =>
            throw new NotSupportedException(
                "Pass an explicit DateTimeOffset instant. Convert wall-clock times with an explicit time zone or offset."
            );

        /// <summary>Enqueues a typed job with a freshly built options snapshot.</summary>
        /// <exception cref="ArgumentNullException">The scheduler or configuration callback is null.</exception>
        public Task<Guid> EnqueueAsync<TArgs>(
            TArgs request,
            Action<JobOptionsBuilder> configure,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(scheduler);
            Argument.IsNotNull(configure);
            var builder = new JobOptionsBuilder();
            configure(builder);
            return scheduler.EnqueueAsync(request, builder.Build(), cancellationToken);
        }

        /// <summary>Enqueues a job that takes no arguments with a freshly built options snapshot.</summary>
        /// <exception cref="ArgumentNullException">The scheduler or configuration callback is null.</exception>
        public Task<Guid> EnqueueAsync<TJob>(
            Action<JobOptionsBuilder> configure,
            CancellationToken cancellationToken = default
        )
            where TJob : IJob
        {
            Argument.IsNotNull(scheduler);
            Argument.IsNotNull(configure);
            var builder = new JobOptionsBuilder();
            configure(builder);
            return scheduler.EnqueueAsync<TJob>(builder.Build(), cancellationToken);
        }

        /// <summary>Schedules a typed job at the supplied instant with a freshly built options snapshot.</summary>
        /// <exception cref="ArgumentNullException">The scheduler or configuration callback is null.</exception>
        public Task<Guid> ScheduleAsync<TArgs>(
            TArgs request,
            DateTimeOffset executionTime,
            Action<JobOptionsBuilder> configure,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(scheduler);
            Argument.IsNotNull(configure);
            var builder = new JobOptionsBuilder();
            configure(builder);
            return scheduler.ScheduleAsync(request, executionTime, builder.Build(), cancellationToken);
        }

        /// <summary>Schedules a job that takes no arguments at the supplied instant with a freshly built options snapshot.</summary>
        /// <exception cref="ArgumentNullException">The scheduler or configuration callback is null.</exception>
        public Task<Guid> ScheduleAsync<TJob>(
            DateTimeOffset executionTime,
            Action<JobOptionsBuilder> configure,
            CancellationToken cancellationToken = default
        )
            where TJob : IJob
        {
            Argument.IsNotNull(scheduler);
            Argument.IsNotNull(configure);
            var builder = new JobOptionsBuilder();
            configure(builder);
            return scheduler.ScheduleAsync<TJob>(executionTime, builder.Build(), cancellationToken);
        }

        /// <summary>Schedules a typed job relative to the scheduler's clock with a freshly built options snapshot.</summary>
        /// <exception cref="ArgumentNullException">The scheduler or configuration callback is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The scheduler rejects a negative or overflowing delay.</exception>
        public Task<Guid> ScheduleAfterAsync<TArgs>(
            TArgs request,
            TimeSpan delay,
            Action<JobOptionsBuilder> configure,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(scheduler);
            Argument.IsNotNull(configure);
            var builder = new JobOptionsBuilder();
            configure(builder);
            return scheduler.ScheduleAfterAsync(request, delay, builder.Build(), cancellationToken);
        }

        /// <summary>Schedules a job that takes no arguments relative to the scheduler's clock with a freshly built options snapshot.</summary>
        /// <exception cref="ArgumentNullException">The scheduler or configuration callback is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The scheduler rejects a negative or overflowing delay.</exception>
        public Task<Guid> ScheduleAfterAsync<TJob>(
            TimeSpan delay,
            Action<JobOptionsBuilder> configure,
            CancellationToken cancellationToken = default
        )
            where TJob : IJob
        {
            Argument.IsNotNull(scheduler);
            Argument.IsNotNull(configure);
            var builder = new JobOptionsBuilder();
            configure(builder);
            return scheduler.ScheduleAfterAsync<TJob>(delay, builder.Build(), cancellationToken);
        }
    }
}
