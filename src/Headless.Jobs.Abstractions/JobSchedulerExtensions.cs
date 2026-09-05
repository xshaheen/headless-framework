// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Jobs.Interfaces;

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

        /// <summary>Enqueues a requestless job with a freshly built options snapshot.</summary>
        /// <exception cref="ArgumentNullException">The scheduler or configuration callback is null.</exception>
        public Task<Guid> EnqueueAsync(
            JobFunctionDescriptor descriptor,
            Action<JobOptionsBuilder> configure,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(scheduler);
            Argument.IsNotNull(configure);
            var builder = new JobOptionsBuilder();
            configure(builder);
            return scheduler.EnqueueAsync(descriptor, builder.Build(), cancellationToken);
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

        /// <summary>Schedules a requestless job at the supplied instant with a freshly built options snapshot.</summary>
        /// <exception cref="ArgumentNullException">The scheduler or configuration callback is null.</exception>
        public Task<Guid> ScheduleAsync(
            JobFunctionDescriptor descriptor,
            DateTimeOffset executionTime,
            Action<JobOptionsBuilder> configure,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(scheduler);
            Argument.IsNotNull(configure);
            var builder = new JobOptionsBuilder();
            configure(builder);
            return scheduler.ScheduleAsync(descriptor, executionTime, builder.Build(), cancellationToken);
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

        /// <summary>Schedules a requestless job relative to the scheduler's clock with a freshly built options snapshot.</summary>
        /// <exception cref="ArgumentNullException">The scheduler or configuration callback is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">The scheduler rejects a negative or overflowing delay.</exception>
        public Task<Guid> ScheduleAfterAsync(
            JobFunctionDescriptor descriptor,
            TimeSpan delay,
            Action<JobOptionsBuilder> configure,
            CancellationToken cancellationToken = default
        )
        {
            Argument.IsNotNull(scheduler);
            Argument.IsNotNull(configure);
            var builder = new JobOptionsBuilder();
            configure(builder);
            return scheduler.ScheduleAfterAsync(descriptor, delay, builder.Build(), cancellationToken);
        }
    }
}
