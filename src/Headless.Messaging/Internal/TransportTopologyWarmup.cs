// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Runtime.ExceptionServices;
using Headless.Hosting;
using Headless.Messaging.Transport;

namespace Headless.Messaging.Internal;

/// <summary>
/// Checks once per host, after it starts, that every registered transport's broker holds the shared topology the
/// registered consumers and messages need, so the host is not ready while a queue <c>resources setup</c> should have
/// created is missing.
/// </summary>
/// <remarks>
/// One batched check per transport replaces the passive declares each consumer client would otherwise make when it
/// connects. Every transport is checked even when an earlier one fails, so the readiness check names each failure.
/// Checking creates nothing, so a retry is safe.
/// </remarks>
internal sealed class TransportTopologyWarmup(
    IEnumerable<ITransportTopologyProvisioner> provisioners,
    MethodMatcherCache selector,
    IMessageMetadataRegistry? messages = null
) : IWarmup
{
    public string Name => "MessagingTopology";

    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        MessagingTopology? topology = null;
        List<Exception>? failures = null;

        foreach (var provisioner in provisioners)
        {
            topology ??= TransportTopologyResourceSource.BuildTopology(selector, messages);

#pragma warning disable CA1031 // False positive: each failure is collected and rethrown below, so one transport's fault does not hide another's.
            try
            {
                await provisioner.CheckAsync(topology, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                (failures ??= []).Add(exception);
            }
#pragma warning restore CA1031
        }

        if (failures is null)
        {
            return;
        }

        if (failures.Count == 1)
        {
            ExceptionDispatchInfo.Throw(failures[0]);
        }

        throw new AggregateException("The shared broker topology check failed.", failures);
    }
}
