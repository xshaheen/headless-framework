// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Serializer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.MiddlewareAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Setup;

public sealed class CoreServiceRegistrationTests
{
    [Fact]
    public void should_insert_middleware_analysis_filter_before_existing_startup_filters()
    {
        // given
        IServiceCollection services = new ServiceCollection();
        services.AddTransient<IStartupFilter, ExistingStartupFilter>();

        // when
        services.AddMiddlewareAnalyzerFilter();

        // then
        services[0].ServiceType.Should().Be<IStartupFilter>();
        services[0].ImplementationType.Should().Be<AnalysisStartupFilter>();
        services[1].ImplementationType.Should().Be<ExistingStartupFilter>();
    }

    [Fact]
    public void should_register_one_shared_json_stack_when_called_repeatedly()
    {
        // given
        IServiceCollection services = new ServiceCollection();

        // when
        services.AddHeadlessJsonService().AddHeadlessJsonService();
        using var provider = services.BuildServiceProvider();

        // then
        var jsonSerializer = provider.GetRequiredService<IJsonSerializer>();
        provider.GetServices<IJsonSerializer>().Should().ContainSingle();
        provider.GetRequiredService<ITextSerializer>().Should().BeSameAs(jsonSerializer);
        provider.GetRequiredService<ISerializer>().Should().BeSameAs(jsonSerializer);
    }

    [Fact]
    public void should_preserve_custom_json_options_provider()
    {
        // given
        IServiceCollection services = new ServiceCollection();
        var custom = Substitute.For<IJsonOptionsProvider>();
        services.AddSingleton(custom);

        // when
        services.AddHeadlessJsonService();
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IJsonOptionsProvider>().Should().BeSameAs(custom);
    }

    [Fact]
    public void should_register_problem_details_and_exception_handler_idempotently()
    {
        // given
        IServiceCollection services = new ServiceCollection();
        services.AddLogging();

        // when
        services.AddHeadlessProblemDetails().AddHeadlessProblemDetails();
        // then
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IProblemDetailsCreator));
        services
            .Should()
            .ContainSingle(descriptor =>
                descriptor.ServiceType == typeof(IExceptionHandler)
                && descriptor.ImplementationType == typeof(HeadlessApiExceptionHandler)
            );
    }

    [Fact]
    public void should_register_a_replaceable_no_op_error_description_localizer_with_problem_details()
    {
        // given
        IServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddSingleton(Substitute.For<Headless.Context.IBuildInformationAccessor>());
        services.AddSingleton(TimeProvider.System);

        // when
        services.AddHeadlessProblemDetails().AddHeadlessProblemDetails();
        using var provider = services.BuildServiceProvider();

        // then
        services.Should().ContainSingle(descriptor => descriptor.ServiceType == typeof(IErrorDescriptionLocalizer));
        provider
            .GetRequiredService<IErrorDescriptionLocalizer>()
            .Localize(new Headless.Primitives.ErrorDescriptor("app:any", "Any."))
            .Should()
            .BeNull();
        provider.GetRequiredService<IProblemDetailsCreator>().Should().NotBeNull();
    }

    [Fact]
    public void should_keep_a_consumer_error_description_localizer_registered_before_problem_details()
    {
        // given
        IServiceCollection services = new ServiceCollection();
        var custom = Substitute.For<IErrorDescriptionLocalizer>();
        services.AddSingleton(custom);

        // when
        services.AddHeadlessProblemDetails();
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IErrorDescriptionLocalizer>().Should().BeSameAs(custom);
    }

    [Fact]
    public void should_resolve_the_problem_details_creator_from_tenant_catalog_registration_alone()
    {
        // given: a catalog host's rejection paths resolve the creator without AddHeadlessProblemDetails()
        IServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();

        // when
        services.AddTenantCatalogResolution();
        using var provider = services.BuildServiceProvider();

        // then
        provider.GetRequiredService<IProblemDetailsCreator>().Should().NotBeNull();
    }

    private sealed class ExistingStartupFilter : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(
            Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next
        )
        {
            return next;
        }
    }
}
