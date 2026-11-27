// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.DistributedLocks;
using Headless.Fencing;
using Headless.Leases;
using Headless.Testing.Tests;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// <c>AddLeases()</c> on a real host: which host shapes start, and how each overload binds the module's options.
/// </summary>
public sealed class LeasesRegistrationTests : TestBase
{
    [Fact]
    public async Task should_start_with_fencing_only_and_report_no_lock_half()
    {
        // given
        await using var app = await LeasesDashboardHost.StartAsync(AbortToken, fencing: true);

        // when
        var summary = await LeasesDashboardHost.GetJsonAsync(app, LeasesDashboardHost.Api + "/summary", AbortToken);

        // then
        var services = app.Services.GetRequiredService<LeasesServices>();
        services.HasFencing.Should().BeTrue();
        services.HasLocks.Should().BeFalse();
        summary.GetProperty("locks").ValueKind.Should().Be(JsonValueKind.Null);
        summary.GetProperty("fencing").GetProperty("registered").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task should_start_with_locks_only_and_report_no_fencing_half()
    {
        // given
        await using var app = await LeasesDashboardHost.StartAsync(AbortToken, locks: true);

        // when
        var summary = await LeasesDashboardHost.GetJsonAsync(app, LeasesDashboardHost.Api + "/summary", AbortToken);

        // then
        var services = app.Services.GetRequiredService<LeasesServices>();
        services.HasFencing.Should().BeFalse();
        services.HasLocks.Should().BeTrue();
        summary.GetProperty("fencing").ValueKind.Should().Be(JsonValueKind.Null);
        summary.GetProperty("locks").GetProperty("registered").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task should_fail_startup_naming_both_remedies_when_the_only_lock_is_the_null_lock()
    {
        // when
        var start = () =>
            LeasesDashboardHost.StartAsync(
                AbortToken,
                before: static services =>
                    services.AddSingleton<IDistributedLock>(new NullDistributedLock(TimeProvider.System))
            );

        // then
        var failure = await start.Should().ThrowAsync<InvalidOperationException>();
        failure.Which.Message.Should().Contain("AddHeadlessFencing").And.Contain("AddHeadlessDistributedLocks");
    }

    [Fact]
    public async Task should_fail_startup_naming_both_remedies_when_the_host_serves_neither_half()
    {
        // when
        var start = () => LeasesDashboardHost.StartAsync(AbortToken);

        // then
        var failure = await start.Should().ThrowAsync<InvalidOperationException>();
        failure.Which.Message.Should().Contain("AddHeadlessFencing").And.Contain("AddHeadlessDistributedLocks");
    }

    [Fact]
    public async Task should_serve_fencing_registered_after_add_leases()
    {
        // given a host that registers Fencing only after the module
        await using var app = await LeasesDashboardHost.StartAsync(
            AbortToken,
            after: static services => services.AddHeadlessFencing(setup => setup.UseInMemory())
        );

        // then
        app.Services.GetRequiredService<LeasesServices>().HasFencing.Should().BeTrue();
        var page = await LeasesDashboardHost.GetJsonAsync(app, LeasesDashboardHost.Api + "/fenced", AbortToken);
        page.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    public static TheoryData<string> Overloads => ["action", "action-with-provider", "configuration"];

    [Theory]
    [MemberData(nameof(Overloads))]
    public async Task should_bind_the_view_policy_through_every_overload(string overload)
    {
        // given
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>(StringComparer.Ordinal) { ["ViewPolicy"] = "LeaseViewers" }
            )
            .Build();

        await using var app = await LeasesDashboardHost.StartAsync(
            AbortToken,
            fencing: true,
            addLeases: dashboard =>
                overload switch
                {
                    "action" => dashboard.AddLeases(static options => options.ViewPolicy = "LeaseViewers"),
                    "action-with-provider" => dashboard.AddLeases(
                        static (options, provider) =>
                        {
                            provider.Should().NotBeNull();
                            options.ViewPolicy = "LeaseViewers";
                        }
                    ),
                    _ => dashboard.AddLeases(configuration),
                }
        );

        // then
        app.Services.GetRequiredService<IOptions<LeasesDashboardOptions>>()
            .Value.ViewPolicy.Should()
            .Be("LeaseViewers");
    }

    [Fact]
    public async Task should_fail_startup_on_a_white_space_view_policy()
    {
        // when
        var start = () =>
            LeasesDashboardHost.StartAsync(
                AbortToken,
                fencing: true,
                addLeases: static dashboard => dashboard.AddLeases(static options => options.ViewPolicy = "  ")
            );

        // then
        (await start.Should().ThrowAsync<Exception>())
            .Which.ToString()
            .Should()
            .Contain("ViewPolicy");
    }

    [Fact]
    public async Task should_list_the_module_in_the_platform_product_with_its_lock_entry()
    {
        // given
        await using var app = await LeasesDashboardHost.StartAsync(AbortToken, fencing: true);

        // when
        var counts = await LeasesDashboardHost.GetJsonAsync(app, "/headless/api/counts?product=platform", AbortToken);

        // then
        var module = app.Services.GetRequiredService<LeasesModule>();
        module.Id.Should().Be("leases");
        module.DisplayName.Should().Be("Leases");
        counts.GetProperty("product").GetString().Should().Be("platform");

        using var client = LeasesDashboardHost.Client(app);
        var cookie = await LeasesDashboardHost.SignInAsync(client, "ops", AbortToken);
        using var page = await LeasesDashboardHost.GetAsync(client, "/headless/", cookie, AbortToken);
        var html = await page.Content.ReadAsStringAsync(AbortToken);
        html.Should().Contain("\"leases\"").And.Contain("\"platform\"").And.Contain("\"lock\"");
    }

    [Fact]
    public void should_treat_a_null_lock_as_no_lock_provider()
    {
        // given
        using var provider = new ServiceCollection()
            .AddSingleton<IDistributedLock>(
                new NullDistributedLock(TimeProvider.System, NullLogger<NullDistributedLock>.Instance)
            )
            .BuildServiceProvider();

        // then
        new LeasesServices(provider)
            .HasLocks.Should()
            .BeFalse();
    }
}
