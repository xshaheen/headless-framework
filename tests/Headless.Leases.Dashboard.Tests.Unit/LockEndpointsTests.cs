// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using Headless.DistributedLocks;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Tests;

/// <summary>The distributed-lock reads, over the in-memory provider and over a substitute shaped like PostgreSQL's.</summary>
public sealed class LockEndpointsTests : TestBase
{
    private const string _Locks = LeasesDashboardHost.Api + "/locks";

    private readonly FakeTimeProvider _time = new(LeasesDashboardHost.Now);

    /// <summary>A lock provider that lists <paramref name="rows"/>, as a session-lock provider lists its own handles.</summary>
    private static IDistributedLock _Listing(IReadOnlyList<DistributedLockInfo> rows)
    {
        var locks = Substitute.For<IDistributedLock>();
        locks.ListActiveLocksAsync(Arg.Any<CancellationToken>()).Returns(rows);
        locks.GetActiveLocksCountAsync(Arg.Any<CancellationToken>()).Returns(rows.Count);
        locks
            .GetLockInfoAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                rows.FirstOrDefault(row => string.Equals(row.Resource, call.Arg<string>(), StringComparison.Ordinal))
            );

        return locks;
    }

    private static DistributedLockInfo _Row(string resource, string? leaseId = "lease", TimeSpan? ttl = null) =>
        new()
        {
            Resource = resource,
            LeaseId = leaseId,
            TimeToLive = ttl,
        };

    [Fact]
    public async Task should_list_the_held_locks_by_resource_with_their_time_to_live_total_and_read_time()
    {
        // given
        await using var app = await LeasesDashboardHost.StartAsync(AbortToken, _time, locks: true);
        var provider = app.Services.GetRequiredService<IDistributedLock>();

        foreach (var resource in new[] { "reports:daily", "orders:rebuild", "invoices:close" })
        {
            await provider.AcquireAsync(
                resource,
                new DistributedLockAcquireOptions { TimeUntilExpires = TimeSpan.FromMinutes(5) },
                AbortToken
            );
        }

        // when
        var page = await LeasesDashboardHost.GetJsonAsync(app, _Locks, AbortToken);

        // then
        var items = page.GetProperty("items").EnumerateArray().ToList();
        items
            .Select(static item => item.GetProperty("resource").GetString())
            .Should()
            .Equal("invoices:close", "orders:rebuild", "reports:daily");
        items.Should().AllSatisfy(static item => item.GetProperty("leaseId").GetString().Should().NotBeNullOrEmpty());
        items
            .Should()
            .AllSatisfy(static item => item.GetProperty("ttlMilliseconds").GetInt64().Should().BeInRange(1, 300_000));
        page.GetProperty("total").GetInt64().Should().Be(3);
        page.GetProperty("readAt").GetDateTimeOffset().Should().Be(_time.GetUtcNow());
        page.GetProperty("nextCursor").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task should_carry_null_for_a_lock_with_no_expiry_and_no_lease_id_and_no_fencing_token()
    {
        // given
        var locks = _Listing([_Row("orders:rebuild", leaseId: null, ttl: null) with { FencingToken = null }]);
        await using var app = await LeasesDashboardHost.StartAsync(
            AbortToken,
            _time,
            before: services => services.AddSingleton(locks)
        );

        // when
        var item = (await LeasesDashboardHost.GetJsonAsync(app, _Locks, AbortToken)).GetProperty("items")[0];

        // then
        item.GetProperty("ttlMilliseconds").ValueKind.Should().Be(JsonValueKind.Null);
        item.GetProperty("leaseId").ValueKind.Should().Be(JsonValueKind.Null);
        item.TryGetProperty("fencingToken", out _).Should().BeFalse();
    }

    [Fact]
    public async Task should_page_a_hundred_at_a_time()
    {
        // given
        var rows = Enumerable
            .Range(0, 250)
            .Select(static i => _Row(string.Create(CultureInfo.InvariantCulture, $"lock:{i:D3}")))
            .Reverse()
            .ToList();
        await using var app = await LeasesDashboardHost.StartAsync(
            AbortToken,
            _time,
            before: services => services.AddSingleton(_Listing(rows))
        );

        // when
        var sizes = new List<int>();
        var seen = new List<string>();
        string? cursor = null;

        do
        {
            var page = await LeasesDashboardHost.GetJsonAsync(
                app,
                cursor is null ? _Locks : _Locks + "?after=" + Uri.EscapeDataString(cursor),
                AbortToken
            );
            var items = page.GetProperty("items").EnumerateArray().ToList();
            sizes.Add(items.Count);
            seen.AddRange(items.Select(static item => item.GetProperty("resource").GetString()!));
            cursor = page.GetProperty("nextCursor").GetString();
        } while (cursor is not null);

        // then
        sizes.Should().Equal(100, 100, 50);
        seen.Should().OnlyHaveUniqueItems().And.HaveCount(250).And.BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public async Task should_look_up_one_held_lock_and_answer_not_found_or_bad_request_otherwise()
    {
        // given
        await using var app = await LeasesDashboardHost.StartAsync(
            AbortToken,
            _time,
            before: services =>
                services.AddSingleton(_Listing([_Row("orders:rebuild", ttl: TimeSpan.FromSeconds(267))]))
        );

        // when
        var found = await LeasesDashboardHost.GetJsonAsync(app, _Locks + "/lock?resource=orders:rebuild", AbortToken);
        using var unheld = await LeasesDashboardHost.SendAsync(app, _Locks + "/lock?resource=nothing", AbortToken);
        using var blank = await LeasesDashboardHost.SendAsync(app, _Locks + "/lock?resource=%20", AbortToken);

        // then
        found.GetProperty("lock").GetProperty("ttlMilliseconds").GetInt64().Should().Be(267_000);
        unheld.StatusCode.Should().Be(HttpStatusCode.NotFound);
        blank.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task should_answer_a_failed_list_with_a_server_error_not_an_empty_page()
    {
        // given
        var locks = Substitute.For<IDistributedLock>();
        locks.ListActiveLocksAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("down"));
        await using var app = await LeasesDashboardHost.StartAsync(
            AbortToken,
            _time,
            before: services => services.AddSingleton(locks)
        );

        // when
        using var response = await LeasesDashboardHost.SendAsync(app, _Locks, AbortToken);

        // then
        ((int)response.StatusCode)
            .Should()
            .BeGreaterThanOrEqualTo(500);
    }

    [Fact]
    public async Task should_only_list_count_and_look_up_and_never_change_a_lock()
    {
        // given
        var locks = _Listing([_Row("orders:rebuild")]);
        await using var app = await LeasesDashboardHost.StartAsync(
            AbortToken,
            _time,
            before: services => services.AddSingleton(locks)
        );

        // when
        await LeasesDashboardHost.GetJsonAsync(app, _Locks, AbortToken);
        await LeasesDashboardHost.GetJsonAsync(app, _Locks + "/lock?resource=orders:rebuild", AbortToken);
        await LeasesDashboardHost.GetJsonAsync(app, "/headless/api/counts?product=platform", AbortToken);

        // then
        locks
            .ReceivedCalls()
            .Select(static call => call.GetMethodInfo().Name)
            .Distinct(StringComparer.Ordinal)
            .Should()
            .BeSubsetOf(["ListActiveLocksAsync", "GetActiveLocksCountAsync", "GetLockInfoAsync"]);
    }

    [Fact]
    public async Task should_answer_not_found_naming_add_headless_distributed_locks_without_a_lock_provider()
    {
        // given
        await using var app = await LeasesDashboardHost.StartAsync(AbortToken, _time, fencing: true);

        // when
        using var response = await LeasesDashboardHost.SendAsync(app, _Locks, AbortToken);

        // then
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync(AbortToken)).Should().Contain("AddHeadlessDistributedLocks");
    }
}
