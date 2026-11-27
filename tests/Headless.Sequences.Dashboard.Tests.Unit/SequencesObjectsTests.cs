// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Security.Claims;
using Headless.Dashboard;
using Headless.Sequences;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class SequencesObjectsTests : TestBase
{
    private static readonly ClaimsPrincipal _User = new(new ClaimsIdentity("test"));

    [Fact]
    public async Task should_resolve_an_exact_id_to_one_counter_and_a_fragment_to_none()
    {
        // given
        await using var host = await SequencesHost.StartAsync(AbortToken);
        await host.IssueGapFreeAsync("acme", "invoice", 2, AbortToken);
        await host.IssueGapFreeAsync("acme", "invoice", 1, AbortToken, partition: "2025");
        var module = host.App.Services.GetRequiredService<SequencesModule>();

        // when
        var exact = await module.ResolveAsync(new() { Query = "acme/invoice/2025", User = _User }, AbortToken);
        var fragment = await module.ResolveAsync(new() { Query = "invoice", User = _User }, AbortToken);
        var missing = await module.ResolveAsync(new() { Query = "acme/invoice/1999", User = _User }, AbortToken);

        // then
        exact.Should().ContainSingle();
        exact[0]
            .Target.Should()
            .Be(
                new ObjectRef
                {
                    Module = "sequences",
                    Kind = "sequence-counter",
                    Id = "acme/invoice/2025",
                }
            );
        exact[0].Title.Should().Be("invoice · acme · 2025");
        exact[0].Status.Should().Be("closed");
        fragment.Should().BeEmpty();
        missing.Should().BeEmpty();
    }

    [Fact]
    public async Task should_count_the_stored_counters_for_the_sidebar()
    {
        // given
        await using var host = await SequencesHost.StartAsync(AbortToken);
        await host.IssueGapFreeAsync("acme", "invoice", 2, AbortToken);
        await host.IssueGapFreeAsync("acme", "invoice", 1, AbortToken, partition: "2025");
        await host.IssueFastAsync("", "ticket", 1, AbortToken);
        var cookie = await host.SignInAsync("viewer", AbortToken);

        // when
        using var response = await host.SendAsync(
            HttpMethod.Get,
            "/headless/api/counts?product=platform",
            cookie,
            AbortToken
        );

        // then
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(AbortToken));
        var count = document.RootElement.GetProperty("counts").GetProperty("counters");
        count.GetProperty("count").GetInt64().Should().Be(3);
        count.GetProperty("capped").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task should_declare_no_command()
    {
        // given
        await using var host = await SequencesHost.StartAsync(AbortToken);

        // when
        var module = host.App.Services.GetRequiredService<SequencesModule>();

        // then
        module.Should().NotBeAssignableTo<IDashboardCommandSource>();
    }

    [Fact]
    public async Task should_resolve_an_id_whose_key_part_the_inspector_refuses_to_none()
    {
        // given: an id in the canonical form whose name is longer than any stored name
        await using var host = await SequencesHost.StartAsync(AbortToken);
        await host.IssueGapFreeAsync("acme", "invoice", 1, AbortToken);
        var module = host.App.Services.GetRequiredService<SequencesModule>();
        var id = "acme/" + new string('n', SequenceFieldLimits.NameMaxLength + 1) + "/2026";

        // when
        var resolve = () => module.ResolveAsync(new() { Query = id, User = _User }, AbortToken);

        // then
        (await resolve.Should().NotThrowAsync())
            .Subject.Should()
            .BeEmpty();
    }

    [Fact]
    public async Task should_refuse_every_read_and_hide_the_count_from_a_viewer_the_module_policy_refuses()
    {
        // given: the shell admits viewers, the module only operators
        await using var host = await SequencesHost.StartAsync(
            AbortToken,
            addModule: static dashboard =>
                dashboard.AddSequences(static options => options.ViewPolicy = SequencesHost.OperatorsPolicy)
        );
        await host.IssueGapFreeAsync("acme", "invoice", 1, AbortToken);
        var viewer = await host.SignInAsync("viewer", AbortToken);
        var id = Uri.EscapeDataString("acme/invoice/2026");
        string[] paths = ["/summary", "/counters", $"/counter?id={id}", $"/counter/partitions?id={id}"];

        // when
        var statuses = new List<HttpStatusCode>();

        foreach (var path in paths)
        {
            using var response = await host.SendAsync(HttpMethod.Get, SequencesHost.Api + path, viewer, AbortToken);
            statuses.Add(response.StatusCode);
        }

        using var counts = await host.SendAsync(
            HttpMethod.Get,
            "/headless/api/counts?product=platform",
            viewer,
            AbortToken
        );

        // then
        statuses.Should().AllSatisfy(static status => ((int)status).Should().BeInRange(400, 499));
        statuses.Should().NotContain(HttpStatusCode.NotFound);
        // Sequences is the product's only module here, so the shell refuses the whole product's counts.
        counts.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await counts.Content.ReadAsStringAsync(AbortToken)).Should().NotContain("counters");
    }
}
