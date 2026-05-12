// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Messaging.Configuration;
using Headless.Messaging.MultiTenancy;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tests.MultiTenancy;

/// <summary>
/// Tests covering <see cref="MultiTenancyMessagingBuilderExtensions.AddTenantPropagation"/>
/// — startup-time validation that fails fast when only the framework's fallback
/// <see cref="NullCurrentTenant"/> is registered.
/// </summary>
public sealed class MultiTenancyMessagingBuilderExtensionsTests : TestBase
{
    [Fact]
    public async Task should_throw_when_AddTenantPropagation_called_without_real_ICurrentTenant_implementation()
    {
        // given — only the framework's fallback NullCurrentTenant is registered.
        // AddTenantPropagation registers its hosted-service validator; StartAsync should
        // throw a diagnostic InvalidOperationException pointing to tenant setup APIs.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant, NullCurrentTenant>();
        var builder = new MessagingBuilder(services);
        builder.AddTenantPropagation();

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        var validator = hostedServices.Single(s => s.GetType().Name == "TenantPropagationStartupValidator");

        // when
        var act = async () => await validator.StartAsync(AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*NullCurrentTenant*AddHeadlessInfrastructure*AddHeadlessMultiTenancy*");
    }

    [Fact]
    public async Task should_not_throw_when_real_ICurrentTenant_is_registered()
    {
        // given — a real (non-null) ICurrentTenant is registered before AddTenantPropagation runs.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentTenant>(Substitute.For<ICurrentTenant>());
        var builder = new MessagingBuilder(services);
        builder.AddTenantPropagation();

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToArray();
        var validator = hostedServices.Single(s => s.GetType().Name == "TenantPropagationStartupValidator");

        // when
        var act = async () => await validator.StartAsync(AbortToken);

        // then
        await act.Should().NotThrowAsync();
    }
}
