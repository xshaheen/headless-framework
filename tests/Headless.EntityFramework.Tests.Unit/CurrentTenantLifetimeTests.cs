// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.EntityFramework;
using Headless.Hosting;
using Headless.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests;

/// <summary>
/// Contexts created outside a scope read the tenant from the root provider, so the host refuses a scoped or
/// transient <see cref="ICurrentTenant"/> at startup instead of capturing one instance for its lifetime.
/// </summary>
public sealed class CurrentTenantLifetimeTests : TestBase
{
    [Theory]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Transient)]
    public async Task should_refuse_to_start_when_current_tenant_is_not_a_singleton(ServiceLifetime lifetime)
    {
        // given
        IServiceCollection services = new ServiceCollection();
        services.Add(new ServiceDescriptor(typeof(ICurrentTenant), typeof(CurrentTenant), lifetime));
        services.AddHeadlessDbContextServices();

        // when
        var act = () => _RunStartingAsync(services);

        // then
        var exception = (await act.Should().ThrowAsync<InvalidServiceLifetimeException>()).Which;
        exception.Message.Should().Contain(nameof(ICurrentTenant)).And.Contain(lifetime.ToString());
    }

    [Fact]
    public async Task should_start_with_the_default_singleton_current_tenant()
    {
        // given
        var services = new ServiceCollection();
        services.AddHeadlessDbContextServices();

        // when
        var act = () => _RunStartingAsync(services);

        // then
        await act.Should().NotThrowAsync();
    }

    // Runs the startup validators the way the host does, before any hosted service starts.
    private static async Task _RunStartingAsync(IServiceCollection services)
    {
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();

        foreach (var service in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await service.StartingAsync(AbortToken);
        }
    }
}
