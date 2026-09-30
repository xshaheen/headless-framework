// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Jobs.Base;

/// <summary>A job that runs without arguments. Declare it with <see cref="JobAttribute"/> on the class.</summary>
/// <remarks>
/// The generated module constructs the class from each run's dependency-injection scope, so constructor parameters
/// resolve like any scoped service. Schedule it by its type, for example
/// <c>scheduler.EnqueueAsync&lt;CloseDay&gt;()</c>.
/// </remarks>
[PublicAPI]
public interface IJob
{
    /// <summary>Runs one attempt of the job.</summary>
    /// <param name="context">Scheduling metadata and the cooperative-cancellation hook for this run.</param>
    /// <param name="cancellationToken">Signalled when the run is cancelled or the host shuts down.</param>
    ValueTask ExecuteAsync(JobContext context, CancellationToken cancellationToken);
}

/// <summary>
/// A job that runs with a typed argument. Declare it with <see cref="JobAttribute"/> on the class.
/// </summary>
/// <typeparam name="TArgs">
/// The argument type. Each argument type belongs to exactly one job, so scheduling code addresses the job by its
/// argument alone, for example <c>scheduler.EnqueueAsync(new InvoiceArgs(id))</c>.
/// </typeparam>
[PublicAPI]
public interface IJob<TArgs>
{
    /// <summary>Runs one attempt of the job.</summary>
    /// <param name="context">The deserialized argument, scheduling metadata, and cancellation hook for this run.</param>
    /// <param name="cancellationToken">Signalled when the run is cancelled or the host shuts down.</param>
    ValueTask ExecuteAsync(JobContext<TArgs> context, CancellationToken cancellationToken);
}
