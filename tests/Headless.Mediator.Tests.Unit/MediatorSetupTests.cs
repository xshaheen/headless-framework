// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Mediator;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class MediatorSetupTests
{
    [Fact]
    public void should_register_tenant_required_behavior_once()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddTenantRequiredBehavior();

        // then
        var descriptor = services.Where(_IsTenantRequiredBehaviorDescriptor).Should().ContainSingle().Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void should_register_tenant_required_behavior_idempotently()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddTenantRequiredBehavior();
        services.AddTenantRequiredBehavior();

        // then
        services.Where(_IsTenantRequiredBehaviorDescriptor).Should().ContainSingle();
    }

    [Fact]
    public void should_return_same_service_collection_instance()
    {
        // given
        var services = new ServiceCollection();

        // when
        var result = services.AddTenantRequiredBehavior();

        // then
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void should_throw_argument_null_exception_when_services_is_null()
    {
        // given
        IServiceCollection? services = null;

        // when
        var action = () => services!.AddTenantRequiredBehavior();

        // then
        action.Should().ThrowExactly<ArgumentNullException>().WithParameterName(nameof(services));
    }

    [Fact]
    public void should_resolve_tenant_required_behavior_from_service_provider()
    {
        // given
        var services = new ServiceCollection();
        services.AddSingleton<ICurrentTenant, NullCurrentTenant>();

        // when
        services.AddTenantRequiredBehavior();

        using var serviceProvider = services.BuildServiceProvider();
        var behaviors = serviceProvider.GetServices<IPipelineBehavior<TestRequest, TestResponse>>();

        // then
        behaviors
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<TenantRequiredBehavior<TestRequest, TestResponse>>();
    }

    private static bool _IsTenantRequiredBehaviorDescriptor(ServiceDescriptor descriptor)
    {
        return descriptor.ServiceType == typeof(IPipelineBehavior<,>)
            && descriptor.ImplementationType == typeof(TenantRequiredBehavior<,>);
    }

    private sealed record TestRequest : IRequest<TestResponse>;

    private sealed record TestResponse;
}
