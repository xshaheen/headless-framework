using Headless.EntityFramework;
using Headless.EntityFramework.MultiTenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tests.Fixture;

namespace Tests;

public sealed class HeadlessTenantWriteGuardTests
{
    [Fact]
    public void cross_tenant_write_exception_should_expose_safe_structural_diagnostics()
    {
        // given
        var exception = new CrossTenantWriteException(
            entityType: typeof(TestEntity).FullName!,
            writeState: "Modified",
            currentTenantAvailable: true,
            entityTenantAvailable: true,
            tenantMatches: false
        );

        // then
        exception.EntityType.Should().Be(typeof(TestEntity).FullName);
        exception.FailureCategory.Should().Be("CrossTenantWrite");
        exception.WriteState.Should().Be("Modified");
        exception.CurrentTenantAvailable.Should().BeTrue();
        exception.EntityTenantAvailable.Should().BeTrue();
        exception.TenantMatches.Should().BeFalse();

        exception.Data["EntityType"].Should().Be(typeof(TestEntity).FullName);
        exception.Data["FailureCategory"].Should().Be("CrossTenantWrite");
        exception.Data["WriteState"].Should().Be("Modified");
        exception.Data["CurrentTenantAvailable"].Should().Be(true);
        exception.Data["EntityTenantAvailable"].Should().Be(true);
        exception.Data["TenantMatches"].Should().Be(false);

        exception.Message.Should().Contain(nameof(TestEntity));
        exception.Message.Should().Contain("Modified");
        exception.Message.Should().NotContain("tenant-a");
        exception.Message.Should().NotContain("tenant-b");
        exception.Data.Values.Cast<object?>().Should().NotContain("tenant-a").And.NotContain("tenant-b");
    }

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
