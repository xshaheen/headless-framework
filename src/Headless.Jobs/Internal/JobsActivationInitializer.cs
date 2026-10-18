// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Hosting;

namespace Headless.Jobs.Internal;

/// <summary>
/// Reports Jobs activation as an <see cref="IInitializer" />, so a caller that waits for every initializer, such as a
/// test server, returns only once the job loops may select work.
/// </summary>
/// <remarks>
/// The activation drain runs after the host start and does not gate HTTP readiness, so without this a test that
/// dispatches work right after its server starts could race the drain. A failed activation faults the wait.
/// </remarks>
internal sealed class JobsActivationInitializer(JobsActivationBarrier barrier) : IInitializer
{
    public bool IsInitialized => barrier.IsActivated;

    public async Task WaitForInitializationAsync(CancellationToken cancellationToken = default)
    {
        if (await barrier.WaitAsync(cancellationToken).ConfigureAwait(false) is { } failure)
        {
            throw new InvalidOperationException("Jobs activation failed: " + failure.Message, failure);
        }
    }
}
