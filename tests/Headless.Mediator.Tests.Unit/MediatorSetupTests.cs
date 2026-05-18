// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Mediator;
using Headless.Mediator.Behaviors;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class MediatorSetupTests
{
    [Fact]
    public void should_register_validation_request_pre_processor_once()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMediatorValidationRequestBehavior();

        // then
        var descriptor = services.Where(_IsValidationRequestPreProcessorDescriptor).Should().ContainSingle().Subject;
        descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void should_register_validation_request_pre_processor_idempotently()
    {
        // given
        var services = new ServiceCollection();

        // when
        services.AddMediatorValidationRequestBehavior();
        services.AddMediatorValidationRequestBehavior();

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
            .Be(ServiceLifetime.Scoped);
        services
            .Where(_IsResponseLoggingBehaviorDescriptor)
            .Should()
            .ContainSingle()
            .Subject.Lifetime.Should()
            .Be(ServiceLifetime.Scoped);
        services
            .Where(_IsCriticalRequestLoggingBehaviorDescriptor)
            .Should()
            .ContainSingle()
            .Subject.Lifetime.Should()
            .Be(ServiceLifetime.Scoped);
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
        var result = services.AddMediatorValidationRequestBehavior().AddMediatorLoggingBehaviors();

        // then
        result.Should().BeSameAs(services);
    }

    [Fact]
    public void should_throw_argument_null_exception_when_services_is_null()
    {
        // given
        IServiceCollection? services = null;

        // when
        var validationAction = () => services!.AddMediatorValidationRequestBehavior();
        var loggingAction = () => services!.AddMediatorLoggingBehaviors();

        // then
        validationAction.Should().ThrowExactly<ArgumentNullException>().WithParameterName(nameof(services));
        loggingAction.Should().ThrowExactly<ArgumentNullException>().WithParameterName(nameof(services));
    }

    [Fact]
    public void should_resolve_validation_request_pre_processor_from_service_provider()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddMediatorValidationRequestBehavior();

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
        var behaviors = serviceProvider.GetServices<IPipelineBehavior<TestRequest, TestResponse>>().ToList();

        // then
        behaviors.Should().ContainSingle(behavior => behavior is RequestLoggingBehavior<TestRequest, TestResponse>);
        behaviors.Should().ContainSingle(behavior => behavior is ResponseLoggingBehavior<TestRequest, TestResponse>);
        behaviors
            .Should()
            .ContainSingle(behavior => behavior is CriticalRequestLoggingBehavior<TestRequest, TestResponse>);
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
