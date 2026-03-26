// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Orm.EntityFramework;
using Headless.Orm.EntityFramework.Contexts;
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

    [Fact]
    public void entity_model_processor_should_be_singleton_to_support_db_context_pooling()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);

        // when
        services.AddHeadlessDbContextServices();

        // then — singleton is required because AddDbContextPool resolves from root provider
        var descriptor = services.Single(d => d.ServiceType == typeof(IHeadlessEntityModelProcessor));
        descriptor.Lifetime.Should().Be(ServiceLifetime.Singleton);

        using var provider = services.BuildServiceProvider();
        var processor = provider.GetRequiredService<IHeadlessEntityModelProcessor>();
        processor.Should().NotBeNull();
    }
}
