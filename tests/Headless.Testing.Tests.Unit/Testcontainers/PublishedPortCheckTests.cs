// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Sockets;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;

namespace Tests.Testcontainers;

public sealed class PublishedPortCheckTests : TestBase
{
    private const int _ContainerPort = 5672;
    private const int _OtherContainerPort = 15672;

    // Privileged ports, so no test can bind them; a released ephemeral port can be handed to a listener in another test
    // process running at the same time, which would then accept the connection.
    private const int _ClosedPort = 1;
    private const int _OtherClosedPort = 2;

    [Fact]
    public async Task should_not_restart_when_the_container_answers()
    {
        // given
        using var own = _Listen();
        var container = _Container(_PortOf(own));
        var check = _Check(_Answers([own]));

        // when
        await check.VerifyAsync(container, cancellationToken: AbortToken);

        // then
        await container.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_start_the_container_and_return_once_it_answers()
    {
        // given
        using var own = _Listen();
        var container = _Container(_PortOf(own));

        // when
        await _Check(_Answers([own])).StartAsync(container, AbortToken);

        // then
        await container.Received(1).StartAsync(Arg.Any<CancellationToken>());
        await container.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_restart_on_another_port_when_another_listener_answers()
    {
        // given - the first start lands on a port another listener holds; the restart takes the next one
        using var foreign = _Listen();
        using var own = _Listen();
        var container = _Container(_PortOf(foreign));
        _MoveOnStart(container, _PortOf(own));

        // when
        await _Check(_Answers([own])).VerifyAsync(container, cancellationToken: AbortToken);

        // then
        await container.Received(1).StopAsync(Arg.Any<CancellationToken>());
        await container.Received(1).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_restart_when_every_connection_is_refused()
    {
        // given - a forward that failed outright stays refused for the container's run
        using var own = _Listen();
        var container = _Container(_ClosedPort);
        _MoveOnStart(container, _PortOf(own));

        // when
        await _Check(_Answers([own])).VerifyAsync(container, cancellationToken: AbortToken);

        // then
        await container.Received(1).StopAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_restart_when_a_listener_accepts_but_never_answers_as_the_container()
    {
        // given - the probe never gets past NotYet on the silent port
        using var silent = _Listen();
        using var own = _Listen();
        var container = _Container(_PortOf(silent));
        _MoveOnStart(container, _PortOf(own));

        // when
        await _Check(_Answers([own], silent: [silent])).VerifyAsync(container, cancellationToken: AbortToken);

        // then
        await container.Received(1).StopAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_restart_when_any_of_several_ports_is_lost()
    {
        // given - the first port is the container's, the second another listener's until the restart
        using var own = _Listen();
        using var foreign = _Listen();
        using var ownSecond = _Listen();
        var container = _Container(_PortOf(own));
        container.GetMappedPublicPort(_OtherContainerPort).Returns((ushort)_PortOf(foreign));
        container
            .StartAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                container.GetMappedPublicPort(_OtherContainerPort).Returns((ushort)_PortOf(ownSecond));
                return Task.CompletedTask;
            });
        var check = new PublishedPortCheck([_ContainerPort, _OtherContainerPort], _Answers([own, ownSecond]))
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(400),
        };

        // when
        await check.VerifyAsync(container, cancellationToken: AbortToken);

        // then
        await container.Received(1).StopAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_name_every_lost_port_when_no_start_reaches_the_container()
    {
        // given
        var container = _Container(_ClosedPort);
        var starts = 0;
        container
            .StartAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                starts++;
                container.GetMappedPublicPort(_ContainerPort).Returns((ushort)(starts == 1 ? _OtherClosedPort : 3));
                return Task.CompletedTask;
            });

        // when
        var act = () => _Check(_Answers()).VerifyAsync(container, cancellationToken: AbortToken);

        // then
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown
            .Which.Message.Should()
            .Contain($":{_ClosedPort} (refused")
            .And.Contain($":{_OtherClosedPort} (refused")
            .And.Contain(":3 (refused");
        await container.Received(2).StartAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_fail_without_restarting_when_no_restart_is_allowed()
    {
        // given - a shared container other runs use
        using var foreign = _Listen();
        var container = _Container(_PortOf(foreign));

        // when
        var act = () => _Check(_Answers()).VerifyAsync(container, allowRestart: false, AbortToken);

        // then
        var thrown = await act.Should().ThrowAsync<InvalidOperationException>();
        thrown.Which.Message.Should().Contain("another listener answered").And.Contain("does not restart");
        await container.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_surface_the_callers_cancellation_instead_of_restarting()
    {
        // given
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var container = _Container(_ClosedPort);

        // when
        var act = () => _Check(_Answers()).VerifyAsync(container, cancellationToken: cancelled.Token);

        // then
        await act.Should().ThrowAsync<OperationCanceledException>();
        await container.DidNotReceive().StopAsync(Arg.Any<CancellationToken>());
    }

    // Short budgets, so a lost port shows within a test; the production ones are seconds to minutes.
    private static PublishedPortCheck _Check(PublishedPortProbe probe)
    {
        return new PublishedPortCheck(_ContainerPort, probe)
        {
            ConnectTimeout = TimeSpan.FromMilliseconds(400),
            ProbeTimeout = TimeSpan.FromMilliseconds(400),
            RefusedTimeout = TimeSpan.FromMilliseconds(600),
            AnswerTimeout = TimeSpan.FromMilliseconds(900),
        };
    }

    // A probe that names the container on the given listeners' ports, reads nothing usable on a silent one, and names
    // another listener anywhere else. It goes by the port the connection reached, as a real probe goes by the reply.
    private static PublishedPortProbe _Answers(TcpListener[]? own = null, TcpListener[]? silent = null)
    {
        var ownPorts = (own ?? []).Select(_PortOf).ToHashSet();
        var silentPorts = (silent ?? []).Select(_PortOf).ToHashSet();

        return (_, _, stream, _) =>
        {
            var port = ((IPEndPoint)stream.Socket.RemoteEndPoint!).Port;

            return Task.FromResult(
                ownPorts.Contains(port) ? PublishedPortReply.Container
                : silentPorts.Contains(port) ? PublishedPortReply.NotYet
                : PublishedPortReply.AnotherListener
            );
        };
    }

    private static void _MoveOnStart(IContainer container, int port)
    {
        container
            .StartAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                container.GetMappedPublicPort(_ContainerPort).Returns((ushort)port);
                return Task.CompletedTask;
            });
    }

    private static IContainer _Container(int hostPort)
    {
        var container = Substitute.For<IContainer>();
        container.Hostname.Returns("127.0.0.1");
        container.Name.Returns("/headless-test");
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
