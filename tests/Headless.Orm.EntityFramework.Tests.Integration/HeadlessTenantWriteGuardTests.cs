using Headless.EntityFramework;
using Headless.EntityFramework.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class HeadlessTenantWriteGuardTests
{
    [Fact]
    public void add_headless_db_context_services_should_register_disabled_tenant_write_guard_options()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessDbContextServices();

        using var provider = services.BuildServiceProvider();

        // then
        var options = provider.GetRequiredService<IOptions<TenantWriteGuardOptions>>().Value;
        options.IsEnabled.Should().BeFalse();
        provider.GetRequiredService<ITenantWriteGuardBypass>().IsActive.Should().BeFalse();
    }

    [Fact]
    public void add_headless_tenant_write_guard_should_enable_options()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessTenantWriteGuard();

        using var provider = services.BuildServiceProvider();

        // then
        var options = provider.GetRequiredService<IOptions<TenantWriteGuardOptions>>().Value;
        options.IsEnabled.Should().BeTrue();
        provider.GetRequiredService<ITenantWriteGuardBypass>().IsActive.Should().BeFalse();
    }

    [Fact]
    public void tenant_write_guard_bypass_should_restore_after_dispose()
    {
        // given
        var bypass = new TenantWriteGuardBypass();

        // when
        using (bypass.BeginBypass())
        {
            // then
            bypass.IsActive.Should().BeTrue();
        }

        bypass.IsActive.Should().BeFalse();
    }

    [Fact]
    public void tenant_write_guard_bypass_should_restore_nested_scopes_in_lifo_order()
    {
        // given
        var bypass = new TenantWriteGuardBypass();

        // when
        using (bypass.BeginBypass())
        {
            bypass.IsActive.Should().BeTrue();

            using (bypass.BeginBypass())
            {
                bypass.IsActive.Should().BeTrue();
            }

            // then
            bypass.IsActive.Should().BeTrue();
        }

        bypass.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task tenant_write_guard_bypass_should_not_leak_into_unrelated_async_flow()
    {
        // given
        var bypass = new TenantWriteGuardBypass();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var unrelated = Task.Run(async () =>
        {
            await release.Task;

            return bypass.IsActive;
        });

        // when
        using (bypass.BeginBypass())
        {
            bypass.IsActive.Should().BeTrue();
        }

        release.SetResult();

        // then
        var leaked = await unrelated;
        leaked.Should().BeFalse();
        bypass.IsActive.Should().BeFalse();
    }
}
