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
    public void should_register_validation_request_pre_processor_once()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddValidationRequestPreProcessor();

        // then
        var descriptor = services.Where(_IsValidationRequestPreProcessorDescriptor).Should().ContainSingle().Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void should_register_validation_request_pre_processor_idempotently()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddValidationRequestPreProcessor();
        services.AddValidationRequestPreProcessor();

        // then
        services.Where(_IsValidationRequestPreProcessorDescriptor).Should().ContainSingle();
    }

    [Fact]
    public void should_register_mediator_logging_behaviors_once()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMediatorLoggingBehaviors();

        // then
        services
            .Where(_IsRequestLoggingBehaviorDescriptor)
            .Should()
            .ContainSingle()
            .Subject.Lifetime.Should()
            .Be(ServiceLifetime.Transient);
        services
            .Where(_IsResponseLoggingBehaviorDescriptor)
            .Should()
            .ContainSingle()
            .Subject.Lifetime.Should()
            .Be(ServiceLifetime.Transient);
        services
            .Where(_IsCriticalRequestLoggingBehaviorDescriptor)
            .Should()
            .ContainSingle()
            .Subject.Lifetime.Should()
            .Be(ServiceLifetime.Transient);
    }

    [Fact]
    public void should_register_mediator_logging_behaviors_idempotently()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMediatorLoggingBehaviors();
        services.AddMediatorLoggingBehaviors();

        // then
        services.Where(_IsRequestLoggingBehaviorDescriptor).Should().ContainSingle();
        services.Where(_IsResponseLoggingBehaviorDescriptor).Should().ContainSingle();
        services.Where(_IsCriticalRequestLoggingBehaviorDescriptor).Should().ContainSingle();
    }

    [Fact]
    public void should_return_same_service_collection_instance()
    {
        // given
        var services = new ServiceCollection();

        // when
        var result = services
            .AddTenantRequiredBehavior()
            .AddValidationRequestPreProcessor()
            .AddMediatorLoggingBehaviors();

        // then
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void should_throw_argument_null_exception_when_services_is_null()
    {
        // given
        IServiceCollection? services = null;

        // when
        var tenantRequiredAction = () => services!.AddTenantRequiredBehavior();
        var validationAction = () => services!.AddValidationRequestPreProcessor();
        var loggingAction = () => services!.AddMediatorLoggingBehaviors();

        // then
        tenantRequiredAction.Should().ThrowExactly<ArgumentNullException>().WithParameterName(nameof(services));
        validationAction.Should().ThrowExactly<ArgumentNullException>().WithParameterName(nameof(services));
        loggingAction.Should().ThrowExactly<ArgumentNullException>().WithParameterName(nameof(services));
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

    [Fact]
    public void should_resolve_validation_request_pre_processor_from_service_provider()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddValidationRequestPreProcessor();

        using var serviceProvider = services.BuildServiceProvider();
        var behaviors = serviceProvider.GetServices<IPipelineBehavior<TestRequest, TestResponse>>();

        // then
        behaviors
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<ValidationRequestPreProcessor<TestRequest, TestResponse>>();
    }

    [Fact]
    public void should_resolve_mediator_logging_behaviors_from_service_provider()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ICurrentUser, NullCurrentUser>();

        // when
        services.AddMediatorLoggingBehaviors();

        using var serviceProvider = services.BuildServiceProvider();
        var behaviors = serviceProvider.GetServices<IPipelineBehavior<TestRequest, TestResponse>>();

        // then
        behaviors
            .Should()
            .ContainSingle(behavior => behavior.GetType() == typeof(RequestLoggingBehavior<TestRequest, TestResponse>));
        behaviors
            .Should()
            .ContainSingle(behavior =>
                behavior.GetType() == typeof(ResponseLoggingBehavior<TestRequest, TestResponse>)
            );
        behaviors
            .Should()
            .ContainSingle(behavior =>
                behavior.GetType() == typeof(CriticalRequestLoggingBehavior<TestRequest, TestResponse>)
            );
    }

    private static bool _IsTenantRequiredBehaviorDescriptor(ServiceDescriptor descriptor)
    {
        return descriptor.ServiceType == typeof(IPipelineBehavior<,>)
            && descriptor.ImplementationType == typeof(TenantRequiredBehavior<,>);
    }

    private static bool _IsValidationRequestPreProcessorDescriptor(ServiceDescriptor descriptor)
    {
        return descriptor.ServiceType == typeof(IPipelineBehavior<,>)
            && descriptor.ImplementationType == typeof(ValidationRequestPreProcessor<,>);
    }

    private static bool _IsRequestLoggingBehaviorDescriptor(ServiceDescriptor descriptor)
    {
        return descriptor.ServiceType == typeof(IPipelineBehavior<,>)
            && descriptor.ImplementationType == typeof(RequestLoggingBehavior<,>);
    }

    private static bool _IsResponseLoggingBehaviorDescriptor(ServiceDescriptor descriptor)
    {
        return descriptor.ServiceType == typeof(IPipelineBehavior<,>)
            && descriptor.ImplementationType == typeof(ResponseLoggingBehavior<,>);
    }

    private static bool _IsCriticalRequestLoggingBehaviorDescriptor(ServiceDescriptor descriptor)
    {
        return descriptor.ServiceType == typeof(IPipelineBehavior<,>)
            && descriptor.ImplementationType == typeof(CriticalRequestLoggingBehavior<,>);
    }

    private sealed record TestRequest : IRequest<TestResponse>;

    private sealed record TestResponse;
}
