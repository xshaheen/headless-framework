// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;

namespace Tests.Testcontainers;

public sealed class ContainerHostPortTests : TestBase
{
    private const int _ContainerPort = 5672;

    // Privileged ports, so no test can bind them; a released ephemeral port can be handed to a listener in another test
    // process running at the same time, which would then accept the connection.
    private const int _ClosedPort = 1;
    private const int _OtherClosedPort = 2;
    private static readonly TimeSpan _Timeout = TimeSpan.FromMilliseconds(400);

    [Fact]
    public async Task should_not_restart_when_the_port_answers()
    {
        using var listener = _Listen();
        var container = _Container(_PortOf(listener));

        await ContainerHostPort.EnsureReachableAsync(container, _ContainerPort, _Timeout, AbortToken);

        await container.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_restart_once_and_succeed_when_the_restart_picks_a_working_port()
    {
        using var listener = _Listen();
        var container = _Container(_ClosedPort);
        container
            .StartAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                container.GetMappedPublicPort(_ContainerPort).Returns((ushort)_PortOf(listener));
                return Task.CompletedTask;
            });

        await ContainerHostPort.EnsureReachableAsync(container, _ContainerPort, _Timeout, AbortToken);

        await container.Received(1).StopAsync(Arg.Any<CancellationToken>());
        await container.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_name_both_ports_when_the_restart_does_not_help()
    {
        var container = _Container(_ClosedPort);
        container
            .StartAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                container.GetMappedPublicPort(_ContainerPort).Returns((ushort)_OtherClosedPort);
                return Task.CompletedTask;
            });

        var act = async () =>
            await ContainerHostPort.EnsureReachableAsync(container, _ContainerPort, _Timeout, AbortToken);

        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain($":{_ClosedPort} ").And.Contain($":{_OtherClosedPort}.");
        await container.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_surface_the_callers_cancellation_instead_of_restarting()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var container = _Container(_ClosedPort);

        var act = async () =>
            await ContainerHostPort.EnsureReachableAsync(container, _ContainerPort, _Timeout, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await container.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
    }

    private static IContainer _Container(int hostPort)
    {
        var container = Substitute.For<IContainer>();
        container.Hostname.Returns("127.0.0.1");
        container.Image.Returns(Substitute.For<IImage>());
        container.GetMappedPublicPort(_ContainerPort).Returns((ushort)hostPort);

        return container;
    }

    private static TcpListener _Listen()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();

        return listener;
    }

    private static int _PortOf(TcpListener listener)
    {
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
