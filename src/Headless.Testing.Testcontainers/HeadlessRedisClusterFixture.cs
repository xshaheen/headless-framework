// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Net;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Headless.Checks;
using Testcontainers.Xunit;
using Xunit;

namespace Headless.Testing.Testcontainers;

/// <summary>
/// A Redis Cluster of three primaries and no replicas in one container, pinned to <see cref="TestImages.Redis"/>, for
/// tests of cluster-only code paths such as per-hash-slot command splitting. Subclass to add per-project setup (for
/// example a connection multiplexer).
/// </summary>
/// <remarks>
/// <para>
/// A cluster client connects to the seed endpoints, then routes each command to the node that the cluster's own
/// topology (<c>CLUSTER NODES</c>/<c>CLUSTER SLOTS</c>) names for its slot. Each node announces the address it binds
/// inside the container by default, which the test host cannot reach. The fixture therefore runs every node in the
/// same container and, once Docker has mapped the ports, makes each node announce <c>127.0.0.1</c> and its
/// host-mapped port. The cluster bus is announced on its container port: gossip between the nodes then dials
/// <c>127.0.0.1:&lt;bus port&gt;</c>, which is the container's own loopback, so the bus ports need no host mapping and
/// the client ports can stay on random host ports. This needs the Docker host to be the local machine; a remote
/// engine fails the start with <see cref="NotSupportedException"/>.
/// </para>
/// <para>
/// The nodes run with <c>cluster-require-full-coverage no</c>, so a slot taken away with
/// <see cref="UnassignSlotAsync"/> fails only the commands for that slot while the rest of the cluster keeps serving.
/// </para>
/// </remarks>
[PublicAPI]
public class HeadlessRedisClusterFixture()
    : ContainerFixture<ContainerBuilder, IContainer>(TestContextMessageSink.Instance)
{
    /// <summary>The number of primaries the cluster runs.</summary>
    public const int PrimaryCount = 3;

    private const int _FirstNodePort = 7000;

    // Redis derives the cluster bus port from the client port by adding 10000.
    private const int _BusPortOffset = 10000;
    private const int _HashSlotCount = 16384;
    private const string _AnnouncedHost = "127.0.0.1";

    private static readonly TimeSpan _FormationTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan _FormationPollInterval = TimeSpan.FromMilliseconds(100);

    private string[] _endpoints = [];

    /// <summary>
    /// Gets the image the fixture runs, <see cref="TestImages.Redis"/> unless overridden. The image must ship
    /// <c>redis-server</c> and <c>redis-cli</c> of Redis 7.0 or later.
    /// </summary>
    protected virtual string Image => TestImages.Redis;

    /// <summary>Gets the <c>host:port</c> endpoint of every primary, as the cluster announces it to clients.</summary>
    public IReadOnlyList<string> Endpoints => _endpoints;

    /// <summary>Gets a StackExchange.Redis connection string that seeds every primary.</summary>
    public string ConnectionString => string.Join(',', _endpoints);

    protected override ContainerBuilder Configure()
    {
        var builder = base.Configure().WithImage(Image).WithEntrypoint("sh", "-c").WithCommand(_StartScript());

        var wait = Wait.ForUnixContainer();

        for (var i = 0; i < PrimaryCount; i++)
        {
            builder = builder.WithPortBinding(_NodePort(i), assignRandomHostPort: true);
            wait = wait.UntilCommandIsCompleted(
                    "redis-cli",
                    "-p",
                    _NodePort(i).ToString(CultureInfo.InvariantCulture),
                    "ping"
                )
                .AddCustomWaitStrategy(new RedisHostPingWaitUntil(_NodePort(i)));
        }

        return builder.WithWaitStrategy(wait);
    }

    /// <inheritdoc />
    protected override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync().ConfigureAwait(false);
        await _EnsureLocalDockerHostAsync().ConfigureAwait(false);

        var cancellationToken = TestContext.Current.CancellationToken;
        var mappedPorts = new int[PrimaryCount];

        for (var i = 0; i < PrimaryCount; i++)
        {
            mappedPorts[i] = Container.GetMappedPublicPort(_NodePort(i));
            var busPort = _NodePort(i) + _BusPortOffset;

            await _NodeCommandAsync(
                    i,
                    [
                        "CONFIG",
                        "SET",
                        "cluster-announce-ip",
                        _AnnouncedHost,
                        "cluster-announce-port",
                        _Invariant(mappedPorts[i]),
                        "cluster-announce-bus-port",
                        _Invariant(busPort),
                    ],
                    cancellationToken
                )
                .ConfigureAwait(false);

            var (first, last) = _SlotRange(i);
            await _NodeCommandAsync(
                    i,
                    ["CLUSTER", "ADDSLOTSRANGE", _Invariant(first), _Invariant(last)],
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        // MEET dials the bus inside the container, where every node listens on the shared loopback.
        for (var i = 1; i < PrimaryCount; i++)
        {
            await _NodeCommandAsync(
                    0,
                    [
                        "CLUSTER",
                        "MEET",
                        _AnnouncedHost,
                        _Invariant(_NodePort(i)),
                        _Invariant(_NodePort(i) + _BusPortOffset),
                    ],
                    cancellationToken
                )
                .ConfigureAwait(false);
        }

        _endpoints = [.. mappedPorts.Select(port => _AnnouncedHost + ":" + _Invariant(port))];

        await _WaitUntilFormedAsync(mappedPorts, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Deletes every key on every primary.</summary>
    /// <remarks>Runs <c>redis-cli FLUSHALL</c> inside the container, so it needs no client connection.</remarks>
    /// <exception cref="InvalidOperationException">A node did not acknowledge the flush.</exception>
    public async Task FlushAllAsync(CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < PrimaryCount; i++)
        {
            await _NodeCommandAsync(i, ["FLUSHALL"], cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Takes <paramref name="slot"/> away from the primary that serves it, so every command for a key in that slot
    /// fails with <c>CLUSTERDOWN Hash slot not served</c> while the other slots keep working. Dispose the result to give
    /// the slot back to the same primary; its keys are kept meanwhile.
    /// </summary>
    /// <param name="slot">The hash slot, from 0 to 16383.</param>
    /// <param name="cancellationToken">Cancels the command.</param>
    /// <returns>A handle that assigns the slot back on dispose.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="slot"/> is not a hash slot.</exception>
    public async Task<IAsyncDisposable> UnassignSlotAsync(int slot, CancellationToken cancellationToken = default)
    {
        Argument.IsInclusiveBetween(slot, 0, _HashSlotCount - 1);

        var node = _NodeOfSlot(slot);
        await _NodeCommandAsync(node, ["CLUSTER", "DELSLOTS", _Invariant(slot)], cancellationToken)
            .ConfigureAwait(false);

        return new SlotAssignment(this, node, slot);
    }

    private static int _NodePort(int node) => _FirstNodePort + node;

    private static string _Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static (int First, int Last) _SlotRange(int node)
    {
        var first = node * _HashSlotCount / PrimaryCount;
        var last = ((node + 1) * _HashSlotCount / PrimaryCount) - 1;

        return (first, last);
    }

    private static int _NodeOfSlot(int slot)
    {
        for (var i = 0; i < PrimaryCount; i++)
        {
            var (first, last) = _SlotRange(i);

            if (slot >= first && slot <= last)
            {
                return i;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(slot));
    }

    private static string _StartScript()
    {
        // Every node but the last runs as a daemon; the last runs in the foreground so the container lives as long as
        // it does. No persistence: a test cluster starts empty every run.
        var nodes = Enumerable
            .Range(0, PrimaryCount)
            .Select(i =>
            {
                var port = _Invariant(_NodePort(i));
                var daemonize = i < PrimaryCount - 1 ? "yes" : "no";

                return $"redis-server --port {port} --cluster-enabled yes --cluster-config-file nodes-{port}.conf "
                    + "--cluster-require-full-coverage no --save '' --appendonly no --protected-mode no "
                    + $"--daemonize {daemonize}";
            });

        return string.Join(" && ", nodes.Select((command, i) => i == PrimaryCount - 1 ? "exec " + command : command));
    }

    private async Task _EnsureLocalDockerHostAsync()
    {
        var hostname = Container.Hostname;
        var addresses = IPAddress.TryParse(hostname, out var address)
            ? [address]
            : await Dns.GetHostAddressesAsync(hostname).ConfigureAwait(false);

        if (!addresses.Any(IPAddress.IsLoopback))
        {
            throw new NotSupportedException(
                $"The Redis Cluster fixture needs a local Docker engine: its nodes announce {_AnnouncedHost}, but the "
                    + $"engine publishes ports on {hostname}."
            );
        }
    }

    private async Task<string> _NodeCommandAsync(int node, string[] command, CancellationToken cancellationToken)
    {
        var result = await Container
            .ExecAsync(["redis-cli", "-p", _Invariant(_NodePort(node)), .. command], cancellationToken)
            .ConfigureAwait(false);
        var output = result.Stdout.Trim();

        // redis-cli exits 0 on an error reply when its output is not a terminal, so read the reply too.
        if (result.ExitCode != 0 || output.StartsWith("ERR", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"redis-cli -p {_NodePort(node)} {string.Join(' ', command)} failed with exit code {result.ExitCode}: {output} {result.Stderr.Trim()}"
                )
            );
        }

        return output;
    }

    // Formed means every node reports cluster_state:ok and already lists every peer at its announced host port, so a
    // client that connects next reads a topology it can route by.
    private async Task _WaitUntilFormedAsync(int[] mappedPorts, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(_FormationTimeout);

        try
        {
            while (!await _IsFormedAsync(mappedPorts, deadline.Token).ConfigureAwait(false))
            {
                await Task.Delay(_FormationPollInterval, deadline.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The Redis Cluster did not form within {_FormationTimeout.TotalSeconds} seconds."
                ),
                e
            );
        }
    }

    private async Task<bool> _IsFormedAsync(int[] mappedPorts, CancellationToken cancellationToken)
    {
        for (var i = 0; i < PrimaryCount; i++)
        {
            var info = await _NodeCommandAsync(i, ["CLUSTER", "INFO"], cancellationToken).ConfigureAwait(false);

            if (!info.Contains("cluster_state:ok", StringComparison.Ordinal))
            {
                return false;
            }

            var nodes = await _NodeCommandAsync(i, ["CLUSTER", "NODES"], cancellationToken).ConfigureAwait(false);

            if (
                !mappedPorts.All(port =>
                    nodes.Contains($"{_AnnouncedHost}:{_Invariant(port)}@", StringComparison.Ordinal)
                )
            )
            {
                return false;
            }
        }

        return true;
    }

    private sealed class SlotAssignment(HeadlessRedisClusterFixture fixture, int node, int slot) : IAsyncDisposable
    {
        // Every dispose awaits one restore: sending ADDSLOTS again would fail on the slot the first one took back.
        private readonly Lazy<Task<string>> _restore = new(() =>
            fixture._NodeCommandAsync(node, ["CLUSTER", "ADDSLOTS", _Invariant(slot)], CancellationToken.None)
        );

        public ValueTask DisposeAsync() => new(_restore.Value);
    }
}
