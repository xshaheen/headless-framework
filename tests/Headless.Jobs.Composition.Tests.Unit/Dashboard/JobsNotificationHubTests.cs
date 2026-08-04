// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Dashboard.Authentication;
using Headless.Jobs.Hubs;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Tests.Dashboard;

public sealed class JobsNotificationHubTests : TestBase
{
    [Fact]
    public async Task should_abort_an_unauthenticated_connection_without_marking_the_context_authenticated()
    {
        var authService = Substitute.For<IAuthService>();
        authService
            .AuthenticateAsync(Arg.Any<HttpContext>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Failure("invalid token"));
        var (hub, context, _, _) = _Create(authService);

        await hub.OnConnectedAsync();

        context.Received(1).Abort();
        context.Items.Should().NotContainKey("authenticated");
    }

    [Fact]
    public async Task should_record_the_authenticated_user_on_connection()
    {
        var authService = Substitute.For<IAuthService>();
        authService
            .AuthenticateAsync(Arg.Any<HttpContext>(), Arg.Any<CancellationToken>())
            .Returns(AuthResult.Success("operator"));
        var (hub, context, _, _) = _Create(authService);

        await hub.OnConnectedAsync();

        context.DidNotReceive().Abort();
        context.Items["username"].Should().Be("operator");
        context.Items["authenticated"].Should().Be(true);
    }

    [Theory]
    [InlineData(true, "GroupJoined")]
    [InlineData(false, "Error")]
    public async Task should_only_join_groups_for_authenticated_connections(bool authenticated, string responseMethod)
    {
        var (hub, context, caller, groups) = _Create(Substitute.For<IAuthService>());
        if (authenticated)
        {
            context.Items["authenticated"] = true;
            context.Items["username"] = "operator";
        }

        await hub.JoinGroup("cron-job-1");

        if (authenticated)
        {
            await groups.Received(1).AddToGroupAsync("connection-1", "cron-job-1", Arg.Any<CancellationToken>());
        }
        else
        {
            await groups
                .DidNotReceive()
                .AddToGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        await caller.Received(1).SendCoreAsync(responseMethod, Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, "GroupLeft")]
    [InlineData(false, "Error")]
    public async Task should_only_leave_groups_for_authenticated_connections(bool authenticated, string responseMethod)
    {
        var (hub, context, caller, groups) = _Create(Substitute.For<IAuthService>());
        if (authenticated)
        {
            context.Items["authenticated"] = true;
            context.Items["username"] = "operator";
        }

        await hub.LeaveGroup("cron-job-1");

        if (authenticated)
        {
            await groups.Received(1).RemoveFromGroupAsync("connection-1", "cron-job-1", Arg.Any<CancellationToken>());
        }
        else
        {
            await groups
                .DidNotReceive()
                .RemoveFromGroupAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }

        await caller.Received(1).SendCoreAsync(responseMethod, Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_report_connection_authentication_and_injected_utc_time()
    {
        var now = new DateTimeOffset(2026, 8, 5, 9, 30, 0, TimeSpan.Zero);
        var timeProvider = new FakeTimeProvider(now);
        var (hub, context, caller, _) = _Create(Substitute.For<IAuthService>(), timeProvider);
        context.Items["authenticated"] = true;
        context.Items["username"] = "operator";
        object? payload = null;
        caller
            .SendCoreAsync("Status", Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                payload = callInfo.ArgAt<object?[]>(1)[0];
                return Task.CompletedTask;
            });

        await hub.GetStatus();

        payload.Should().NotBeNull();
        _Read(payload!, "connectionId").Should().Be("connection-1");
        _Read(payload!, "authenticated").Should().Be(true);
        _Read(payload!, "username").Should().Be("operator");
        _Read(payload!, "timestamp").Should().Be(now.UtcDateTime);
    }

    private static (
        JobsNotificationHub Hub,
        HubCallerContext Context,
        IClientProxy Caller,
        IGroupManager Groups
    ) _Create(IAuthService authService, TimeProvider? timeProvider = null)
    {
        var context = Substitute.For<HubCallerContext>();
        var items = new Dictionary<object, object?>();
        var features = new FeatureCollection();
        features.Set<IHttpContextFeature>(new TestHttpContextFeature { HttpContext = new DefaultHttpContext() });
        context.ConnectionId.Returns("connection-1");
        context.Items.Returns(items);
        context.Features.Returns(features);

        var caller = Substitute.For<ISingleClientProxy>();
        caller
            .SendCoreAsync(Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var clients = Substitute.For<IHubCallerClients>();
        clients.Caller.Returns(caller);
        var groups = Substitute.For<IGroupManager>();

        var hub = new JobsNotificationHub(
            NullLogger<JobsNotificationHub>.Instance,
            authService,
            timeProvider ?? TimeProvider.System
        )
        {
            Context = context,
            Clients = clients,
            Groups = groups,
        };

        return (hub, context, caller, groups);
    }

    private static object? _Read(object value, string propertyName)
    {
        return value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.GetValue(value);
    }

    private sealed class TestHttpContextFeature : IHttpContextFeature
    {
        public HttpContext? HttpContext { get; set; }
    }
}
