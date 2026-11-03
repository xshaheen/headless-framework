// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using Headless.Testing.Testcontainers;

namespace Tests;

/// <summary>
/// Builds a single-node KRaft Kafka whose client listeners Docker publishes on random host ports, and whose advertised
/// addresses are decided once those ports are known. A Kafka client connects to the address the broker advertises, not
/// to the one it bootstrapped from, so the advertised address must be one the test can reach; choosing it after Docker
/// has bound the ports means no host port is reserved ahead of the bind.
/// </summary>
internal static class SingleNodeKafka
{
    private const int _InterBrokerPort = 9093;
    private const int _ControllerPort = 9094;

    // Any fixed base64 UUID; a KRaft log is formatted once for the cluster it names, and this broker never outlives its test.
    private const string _ClusterId = "4L6g3nShT-eMCtK--X86sw";

    private const string _StartScriptPath = "/headless-kafka-start.sh";

    /// <summary>Creates the container builder.</summary>
    /// <param name="clientListeners">The name and container port of each listener a client connects to.</param>
    /// <param name="advertiseAsync">
    /// Returns, once Docker has published the ports, the advertised address of every client listener in
    /// <c>KAFKA_ADVERTISED_LISTENERS</c> form (<c>NAME://host:port,...</c>). It runs on the first start only, and the
    /// broker keeps those addresses across a stop and start, so an address that must survive a restart cannot name a
    /// port Docker publishes per start.
    /// </param>
    public static ContainerBuilder CreateBuilder(
        IReadOnlyList<(string Name, int Port)> clientListeners,
        Func<IContainer, CancellationToken, Task<string>> advertiseAsync
    )
    {
        var listeners = string.Join(
            ',',
            clientListeners.Select(static listener => $"{listener.Name}://0.0.0.0:{listener.Port}")
        );
        var protocols = string.Join(',', clientListeners.Select(static listener => $"{listener.Name}:PLAINTEXT"));
        var scriptWritten = false;

        var builder = new ContainerBuilder(TestImages.Kafka);

        foreach (var (_, port) in clientListeners)
        {
            builder = builder.WithPortBinding(port, assignRandomHostPort: true);
        }

        return builder
            .WithEnvironment("CLUSTER_ID", _ClusterId)
            .WithEnvironment("KAFKA_NODE_ID", "1")
            .WithEnvironment("KAFKA_PROCESS_ROLES", "broker,controller")
            .WithEnvironment("KAFKA_CONTROLLER_QUORUM_VOTERS", $"1@localhost:{_ControllerPort}")
            .WithEnvironment("KAFKA_CONTROLLER_LISTENER_NAMES", "CONTROLLER")
            .WithEnvironment("KAFKA_INTER_BROKER_LISTENER_NAME", "BROKER")
            .WithEnvironment(
                "KAFKA_LISTENERS",
                $"{listeners},BROKER://0.0.0.0:{_InterBrokerPort},CONTROLLER://0.0.0.0:{_ControllerPort}"
            )
            .WithEnvironment(
                "KAFKA_LISTENER_SECURITY_PROTOCOL_MAP",
                $"{protocols},BROKER:PLAINTEXT,CONTROLLER:PLAINTEXT"
            )
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_OFFSETS_TOPIC_NUM_PARTITIONS", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR", "1")
            .WithEnvironment("KAFKA_TRANSACTION_STATE_LOG_MIN_ISR", "1")
            .WithEnvironment("KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS", "0")
            // The broker waits for the script the startup callback writes, because the advertised addresses are known
            // only after Docker publishes the ports. A restart finds the script already there and runs it at once.
            .WithEntrypoint("/bin/sh", "-c")
            .WithCommand($"while [ ! -f {_StartScriptPath} ]; do sleep 0.1; done; exec {_StartScriptPath}")
            .WithStartupCallback(
                async (container, cancellationToken) =>
                {
                    // Written once: a restart is already running the script, and rewriting a script bash is reading
                    // can corrupt the run.
                    if (scriptWritten)
                    {
                        return;
                    }

                    var advertised = await advertiseAsync(container, cancellationToken).ConfigureAwait(false);
                    var script =
                        "#!/bin/bash\n"
                        + $"export KAFKA_ADVERTISED_LISTENERS={advertised},BROKER://localhost:{_InterBrokerPort}\n"
                        + "exec /etc/confluent/docker/run\n";
                    await container
                        .CopyAsync(
                            Encoding.UTF8.GetBytes(script),
                            _StartScriptPath,
                            fileMode: Unix.FileMode755,
                            ct: cancellationToken
                        )
                        .ConfigureAwait(false);
                    scriptWritten = true;
                }
            )
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Kafka Server started"));
    }
}
