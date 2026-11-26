// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.DevInbox.Dashboard;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>The change source the shell polls: each new capture once, with its list projection.</summary>
public sealed class ChangeSourceTests : TestBase
{
    [Fact]
    public async Task should_read_a_new_email_once_with_its_projection_and_version()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        IDashboardChangeSource source = app.Services.GetRequiredService<DevInboxModule>();
        await DevInboxTestHost.SendSmsAsync(app, "before the cursor", AbortToken);
        var start = await source.GetStartCursorAsync(AbortToken);
        var email = await DevInboxTestHost.SendEmailAsync(app, "Welcome", AbortToken);

        // when
        var first = await source.ReadChangesAsync(start, AbortToken);
        var second = await source.ReadChangesAsync(first.Cursor, AbortToken);

        // then
        var change = first.Changes.Should().ContainSingle().Subject;
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
        change.Version.Should().Be(email.Sequence.ToString(CultureInfo.InvariantCulture));
        change.Projection!.Value.GetProperty("subject").GetString().Should().Be("Welcome");
        change.Projection.Value.TryGetProperty("textBody", out _).Should().BeFalse();
        second.Changes.Should().BeEmpty();
    }

    [Fact]
    public async Task should_start_again_from_now_on_a_malformed_cursor()
    {
        // given
        await using var app = await DevInboxTestHost.StartAsync(AbortToken);
        IDashboardChangeSource source = app.Services.GetRequiredService<DevInboxModule>();
        await DevInboxTestHost.SendEmailAsync(app, "old", AbortToken);

        // when
        var batch = await source.ReadChangesAsync("not-a-cursor", AbortToken);
        await DevInboxTestHost.SendEmailAsync(app, "new", AbortToken);
        var next = await source.ReadChangesAsync(batch.Cursor, AbortToken);

        // then
        batch.Changes.Should().BeEmpty();
        next.Changes.Select(static c => c.Projection!.Value.GetProperty("subject").GetString()).Should().Equal("new");
    }
}
