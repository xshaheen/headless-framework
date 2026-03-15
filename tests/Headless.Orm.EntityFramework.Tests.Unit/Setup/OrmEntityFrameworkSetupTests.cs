// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Orm.EntityFramework;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Setup;

public sealed class OrmEntityFrameworkSetupTests
{
    [Fact]
    public void add_headless_db_context_services_should_register_current_tenant_by_default()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddHeadlessDbContextServices();

        using var serviceProvider = services.BuildServiceProvider();
        var currentTenant = serviceProvider.GetRequiredService<ICurrentTenant>();

        // then
        currentTenant.Should().BeOfType<CurrentTenant>();
        currentTenant.Id.Should().BeNull();
        currentTenant.IsAvailable.Should().BeFalse();
    }
}
