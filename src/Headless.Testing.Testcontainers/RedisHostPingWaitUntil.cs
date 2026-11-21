// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Testcontainers.Redis;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// Ready once this container's own Redis answers through the host-mapped port. The stock check runs <c>redis-cli ping</c>
/// inside the container, which passes before a runtime such as OrbStack or Docker Desktop forwards the port, and any
/// Redis answers <c>PING</c>: another test's Redis, or a forwarder to one, that holds the same port on the host would
/// pass too. This check compares the <c>run_id</c> the port answers with the one read inside the container.
/// </summary>
/// <remarks>
/// A wait strategy runs inside the start, so it cannot start the container again on another port: when another listener
/// answers, it fails the start with <see cref="InvalidOperationException"/> instead of letting the test reach (and
/// flush) someone else's Redis. Start through <see cref="PublishedPortCheck.StartAsync"/> with
/// <see cref="PublishedPortProbes.RedisRunId"/> to recover by restarting instead.
/// </remarks>
internal sealed class RedisHostPingWaitUntil(int containerPort = RedisBuilder.RedisPort) : IWaitUntil
{
    private static readonly TimeSpan _ConnectTimeout = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan _ProbeTimeout = TimeSpan.FromSeconds(2);

    public async Task<bool> UntilAsync(IContainer container)
    {
        var (_, reply) = await PublishedPortCheck
            .ProbeOnceAsync(
                container,
                containerPort,
                PublishedPortProbes.RedisRunId,
                _ConnectTimeout,
                _ProbeTimeout,
                CancellationToken.None
            )
            .ConfigureAwait(false);

        return reply switch
        {
            PublishedPortReply.Container => true,
            PublishedPortReply.AnotherListener => throw new InvalidOperationException(
                $"Another Redis or listener answered on {container.Hostname}:{container.GetMappedPublicPort(containerPort).ToString(CultureInfo.InvariantCulture)}, "
                    + $"the host port published for {container.Name}, so no client could reach it. Start it through PublishedPortCheck to restart on another port."
            ),
            // Not reachable yet; Testcontainers polls again until the container's start timeout.
            _ => false,
        };
    }
}
