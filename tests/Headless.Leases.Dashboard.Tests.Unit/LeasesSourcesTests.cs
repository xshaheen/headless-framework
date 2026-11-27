// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Fencing;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;

namespace Tests;

/// <summary>The sidebar count and the palette lookup, read through the shell's own endpoints.</summary>
public sealed class LeasesSourcesTests : TestBase
{
    private const string _Counts = "/headless/api/counts?product=platform";

    private readonly FakeTimeProvider _time = new(LeasesDashboardHost.Now);

    private static JsonElement _Count(JsonElement answer) => answer.GetProperty("counts").GetProperty("leases");

    private static async Task<List<(string Kind, string Id, string? Status)>> _ResolveAsync(
        Microsoft.AspNetCore.Builder.WebApplication app,
        string query,
        CancellationToken cancellationToken
    )
    {
        using var client = LeasesDashboardHost.Client(app);
        var cookie = await LeasesDashboardHost.SignInAsync(client, "ops", cancellationToken);
        using var response = await LeasesDashboardHost.PostAsync(
            client,
            "/headless/api/resolve",
            new { query },
            cookie,
            cancellationToken
        );
        var answer = await LeasesDashboardHost.ReadAsync(response, cancellationToken);
        var module = answer
            .GetProperty("modules")
            .EnumerateArray()
            .SingleOrDefault(static item =>
                string.Equals(item.GetProperty("id").GetString(), "leases", StringComparison.Ordinal)
            );

        if (module.ValueKind == JsonValueKind.Undefined)
        {
            return [];
        }

        return
        [
            .. module
                .GetProperty("items")
                .EnumerateArray()
                .Select(static item =>
                    (
                        item.GetProperty("target").GetProperty("kind").GetString()!,
                        item.GetProperty("target").GetProperty("id").GetString()!,
                        item.TryGetProperty("status", out var status) ? status.GetString() : null
                    )
                ),
        ];
    }

    [Fact]
    public async Task should_count_the_live_leases_only()
    {
        // given
        await using var app = await LeasesDashboardHost.StartAsync(AbortToken, _time, fencing: true, locks: true);

        for (var i = 0; i < 3; i++)
        {
            await LeasesDashboardHost.GrantAsync(app, null, "cleanup", $"live/{i}", AbortToken);
        }

        await LeasesDashboardHost.GrantAsync(app, null, "cleanup", "short/1", AbortToken, TimeSpan.FromSeconds(1));
        await LeasesDashboardHost.GrantAsync(app, "acme", "cleanup", "short/2", AbortToken, TimeSpan.FromSeconds(1));
        _time.Advance(TimeSpan.FromSeconds(2));

        // when
        var count = _Count(await LeasesDashboardHost.GetJsonAsync(app, _Counts, AbortToken));

        // then
        count.GetProperty("count").GetInt64().Should().Be(3);
        count.GetProperty("capped").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task should_mark_a_capped_live_count()
    {
        // given
        var inspector = Substitute.For<IFencedLeaseInspector>();
        inspector
            .CountAsync(Arg.Any<FencedLeaseQuery>(), Arg.Any<CancellationToken>())
            .Returns(
                new FencedLeaseCounts(
                    new LeaseCount(LeaseCount.DefaultCap, Capped: true),
                    new LeaseCount(0, Capped: false),
                    new LeaseCount(0, Capped: false)
                )
            );
        await using var app = await LeasesDashboardHost.StartAsync(
            AbortToken,
            _time,
            before: services => services.AddSingleton(inspector)
        );

        // when
        var count = _Count(await LeasesDashboardHost.GetJsonAsync(app, _Counts, AbortToken));

        // then
        count.GetProperty("capped").GetBoolean().Should().BeTrue();
        count.GetProperty("count").GetInt64().Should().Be(10_000);
    }

    [Fact]
    public async Task should_count_the_held_locks_on_a_host_without_fencing()
    {
        // given
        await using var app = await LeasesDashboardHost.StartAsync(AbortToken, _time, locks: true);
        var provider = app.Services.GetRequiredService<IDistributedLock>();

        for (var i = 0; i < 4; i++)
        {
            await provider.AcquireAsync($"lock:{i}", cancellationToken: AbortToken);
        }

        // when
        var count = _Count(await LeasesDashboardHost.GetJsonAsync(app, _Counts, AbortToken));

        // then
        count.GetProperty("count").GetInt64().Should().Be(4);
    }

    [Fact]
    public async Task should_resolve_an_exact_lease_id_and_an_exact_lock_resource_and_both_when_one_string_is_both()
    {
        // given
        await using var app = await LeasesDashboardHost.StartAsync(AbortToken, _time, fencing: true, locks: true);
        await LeasesDashboardHost.GrantAsync(app, "acme", "cleanup", "batch", AbortToken);
        var provider = app.Services.GetRequiredService<IDistributedLock>();
        await provider.AcquireAsync("orders:rebuild", cancellationToken: AbortToken);
        // A lock whose resource is also a lease id, so one query matches both kinds.
        await provider.AcquireAsync("acme/cleanup/batch", cancellationToken: AbortToken);

        // when
        var lease = await _ResolveAsync(app, "acme/cleanup/batch", AbortToken);
        var lockOnly = await _ResolveAsync(app, "orders:rebuild", AbortToken);

        // then
        lease
            .Should()
            .BeEquivalentTo([
                ("fenced-lease", "acme/cleanup/batch", "Current"),
                ("lock", "acme/cleanup/batch", "Held"),
            ]);
        lockOnly.Should().Equal(("lock", "orders:rebuild", "Held"));
    }

    [Theory]
    [InlineData("nothing")]
    [InlineData("a/b")]
    [InlineData("acme/cleanup")]
    public async Task should_resolve_nothing_for_text_that_names_no_lease_or_lock(string query)
    {
        // given
        await using var app = await LeasesDashboardHost.StartAsync(AbortToken, _time, fencing: true, locks: true);
        await LeasesDashboardHost.GrantAsync(app, "acme", "cleanup", "batch", AbortToken);

        // when
        var objects = await _ResolveAsync(app, query, AbortToken);

        // then
        objects.Should().BeEmpty();
    }
}
