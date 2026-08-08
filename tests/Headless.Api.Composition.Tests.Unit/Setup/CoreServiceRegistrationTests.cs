// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;
using Headless.Api;
using Headless.Api.Diagnostics;
using Headless.Api.Middlewares;
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
