// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.Dashboard;
using Headless.DevInbox.Dashboard;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Delete through the shell's command path: confirmed, logged, published, and refused to a viewer.</summary>
public sealed class CommandTests : TestBase
{
    private const string _DeleteEmail = DevInboxTestHost.Commands + "/dev-inbox.email.delete";

    private readonly RecordingOperatorLog _log = new();

    [Fact]
    public async Task should_delete_an_email_log_the_command_and_publish_its_removal()
    {
        // given
        var published = new RecordingEventPublisher();
        await using var app = await DevInboxTestHost.StartAsync(
            AbortToken,
            roles: true,
            log: _log,
            configure: services => services.AddSingleton<IDashboardEventPublisher>(published)
        );
        using var client = DevInboxTestHost.Client(app);
        var cookie = await DevInboxTestHost.SignInAsync(client, "ops", AbortToken);
        var email = await DevInboxTestHost.SendEmailAsync(app, "Reset", AbortToken);

        // when
        using var response = await DevInboxTestHost.PostAsync(
            client,
            _DeleteEmail,
            new { ids = new[] { email.Id.ToString("D") }, confirmed = true },
            AbortToken,
            cookie
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var row = (await DevInboxTestHost.ReadAsync(response, AbortToken)).GetProperty("rows")[0];
        row.GetProperty("outcome").GetString().Should().Be("done");
        DevInboxTestHost.EmailStore(app).Get(email.Id).Should().BeNull();
        var entry = _log.Entries.Should().ContainSingle().Subject;
        entry.Actor.Should().Be("ops");
        entry.CommandId.Should().Be("dev-inbox.email.delete");
        entry.Ids.Should().Equal(email.Id.ToString("D"));
        var change = published.Changes.Should().ContainSingle().Subject;
        change
            .Target.Should()
            .Be(
                new ObjectRef
                {
                    Module = "dev-inbox",
                    Kind = "email",
                    Id = email.Id.ToString("D"),
                }
            );
        change.Projection.Should().BeNull();
        long.Parse(change.Version, CultureInfo.InvariantCulture).Should().BeGreaterThan(email.Sequence);
    }

    [Fact]
    public async Task should_skip_an_email_that_is_already_deleted()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken, roles: true, log: _log);
        using var client = DevInboxTestHost.Client(app);
        var cookie = await DevInboxTestHost.SignInAsync(client, "ops", AbortToken);
        var email = await DevInboxTestHost.SendEmailAsync(app, "Reset", AbortToken);
        DevInboxTestHost.EmailStore(app).Delete(email.Id);

        // when
        using var response = await DevInboxTestHost.PostAsync(
            client,
            _DeleteEmail,
            new { ids = new[] { email.Id.ToString("D") }, confirmed = true },
            AbortToken,
            cookie
        );

        // then
        var row = (await DevInboxTestHost.ReadAsync(response, AbortToken)).GetProperty("rows")[0];
        row.GetProperty("outcome").GetString().Should().Be("skipped");
    }

    [Fact]
    public async Task should_refuse_a_viewer_and_keep_the_email()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken, roles: true, log: _log);
        using var client = DevInboxTestHost.Client(app);
        var cookie = await DevInboxTestHost.SignInAsync(client, "viewer", AbortToken);
        var email = await DevInboxTestHost.SendEmailAsync(app, "Reset", AbortToken);

        // when
        using var response = await DevInboxTestHost.PostAsync(
            client,
            _DeleteEmail,
            new { ids = new[] { email.Id.ToString("D") }, confirmed = true },
            AbortToken,
            cookie
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        DevInboxTestHost.EmailStore(app).Get(email.Id).Should().NotBeNull();
        _log.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task should_offer_only_the_commands_of_the_configured_channels()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken, emails: false, sms: false);
        using var client = DevInboxTestHost.Client(app);

        // when
        var commands = await DevInboxTestHost.GetJsonAsync(client, DevInboxTestHost.Commands, AbortToken);

        // then
        commands
            .GetProperty("commands")
            .EnumerateArray()
            .Select(static command => command.GetProperty("id").GetString())
            .Should()
            .BeEquivalentTo("dev-inbox.push.delete", "dev-inbox.push.clear");
    }

    [Fact]
    public async Task should_declare_delete_as_a_confirmed_change_with_no_inverse_and_clear_as_needing_a_reason()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        IDashboardCommandSource module = app.Services.GetRequiredService<DevInboxModule>();

        // when
        var commands = module.Commands.ToDictionary(static command => command.Id, StringComparer.Ordinal);

        // then
        commands["dev-inbox.sms.delete"].Confirmation.Should().Be(DashboardConfirmation.Confirm);
        commands["dev-inbox.sms.delete"].InverseCommandId.Should().BeNull();
        commands["dev-inbox.sms.delete"].RequiresReason.Should().BeFalse();
        commands["dev-inbox.sms.clear"].RequiresReason.Should().BeTrue();
        commands["dev-inbox.sms.clear"].ObjectKind.Should().Be("sms");
    }
}

/// <summary>Records every change published to this node's streams.</summary>
internal sealed class RecordingEventPublisher : IDashboardEventPublisher
{
    private readonly Lock _lock = new();
    private readonly List<DashboardChange> _changes = [];

    public IReadOnlyList<DashboardChange> Changes
    {
        get
        {
            lock (_lock)
            {
                return [.. _changes];
            }
        }
    }

    public void Publish(DashboardChange change)
    {
        lock (_lock)
        {
            _changes.Add(change);
        }
    }
}
