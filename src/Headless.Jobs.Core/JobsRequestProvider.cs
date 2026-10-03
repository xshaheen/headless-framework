// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Jobs.Base;
using Headless.Jobs.Instrumentation;
using Headless.Jobs.Interfaces.Managers;
using Headless.Jobs.Models;
using Headless.Reliability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Jobs;

/// <summary>
/// Helper for deserializing a typed argument payload for a job run.
/// </summary>
public static class JobsRequestProvider
{
    /// <summary>
    /// Reads and deserializes the typed request payload stored for the current job execution. Returns
    /// <see langword="default"/> only when no payload is stored for the job.
    /// </summary>
    /// <typeparam name="T">The expected request type.</typeparam>
    /// <param name="context">The current job execution context.</param>
    /// <param name="cancellationToken">Token that can abort the persistence read.</param>
    /// <returns>The deserialized request, or <see langword="default"/> when the job stored no request.</returns>
    /// <remarks>
    /// A read or deserialization failure propagates and therefore fails the attempt, which the retry pipeline
    /// classifies like any other job failure. It is never converted into a handler invocation with a default
    /// payload: doing so would either surface the infrastructure fault as a misleading
    /// <see cref="NullReferenceException"/> from consumer code or record a job as succeeded although its payload
    /// was never processed.
    /// </remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was signalled.</exception>
    public static async Task<T?> GetRequestAsync<T>(JobContext context, CancellationToken cancellationToken)
    {
        var internalJobsManager = context.ServiceScope.ServiceProvider.GetRequiredService<IInternalJobManager>();

        try
        {
            return await internalJobsManager
                .GetRequestAsync<T>(context.Id, context.Type, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Observability only — the failure is rethrown. This records the request type, which the generic job
            // failure record does not carry. Resolved with GetService so a missing instrumentation registration
            // cannot replace the real payload failure with a DI exception.
            context
                .ServiceScope.ServiceProvider.GetService<IJobsInstrumentation>()
                ?.LogRequestDeserializationFailure(
                    typeof(T).FullName!,
                    context.FunctionName,
                    context.Id,
                    context.Type,
                    e
                );

            throw;
        }
    }
}
