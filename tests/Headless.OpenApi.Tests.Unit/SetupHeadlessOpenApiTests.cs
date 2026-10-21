// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Api;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests;

public sealed class SetupHeadlessOpenApiTests : TestBase
{
    [Fact]
    public void should_reject_missing_arguments()
    {
        IServiceCollection services = null!;
        var valid = new ServiceCollection();

        FluentActions.Invoking(() => services.AddHeadlessOpenApi()).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => valid.AddHeadlessOpenApi(" ")).Should().Throw<ArgumentException>();
        FluentActions
            .Invoking(() => services.AddHeadlessApiSurfaceDocuments([]))
            .Should()
            .Throw<ArgumentNullException>();
        FluentActions.Invoking(() => valid.AddHeadlessApiSurfaceDocuments([" "])).Should().Throw<ArgumentException>();
        FluentActions
            .Invoking(() => services.ConfigureHeadlessOpenApi(_ => { }))
            .Should()
            .Throw<ArgumentNullException>();
        FluentActions
            .Invoking(() => valid.ConfigureHeadlessOpenApi((Action<HeadlessOpenApiOptions>)null!))
            .Should()
            .Throw<ArgumentNullException>();
        FluentActions
            .Invoking(() => services.ConfigureHeadlessOpenApi((_, _) => { }))
            .Should()
            .Throw<ArgumentNullException>();
        FluentActions
            .Invoking(() => valid.ConfigureHeadlessOpenApi((Action<HeadlessOpenApiOptions, IServiceProvider>)null!))
            .Should()
            .Throw<ArgumentNullException>();
        FluentActions
            .Invoking(() => ((OpenApiOptions)null!).AddHeadlessTransformers())
            .Should()
            .Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_register_options_validation_once_for_several_documents()
    {
        var services = new ServiceCollection();

        services.AddHeadlessOpenApi();
        services.AddHeadlessOpenApi("v2");
        services.AddHeadlessApiSurfaceDocuments(["portal"]);

        services.Count(d => d.ServiceType == typeof(IValidator<HeadlessOpenApiOptions>)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(IValidateOptions<HeadlessOpenApiOptions>)).Should().Be(1);
    }

    [Fact]
    public void should_register_options_validation_for_surface_documents_alone()
    {
        var services = new ServiceCollection();

        services.AddHeadlessApiSurfaceDocuments(["portal"]);

        services.Count(d => d.ServiceType == typeof(IValidateOptions<HeadlessOpenApiOptions>)).Should().Be(1);
    }

    [Fact]
    public void should_register_only_the_named_surface_documents()
    {
        var services = new ServiceCollection();
        services.AddHeadlessApiSurfaces(surfaces => surfaces.Add("portal").Add("console"));

        services.AddHeadlessApiSurfaceDocuments(["Portal"]);

        var documentKeys = services
            .Where(d => d.IsKeyedService)
            .Select(d => d.ServiceKey)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        documentKeys.Should().Contain("portal").And.NotContain("console");
    }

    [Fact]
    public void should_run_the_document_callback_after_adding_the_transformers()
    {
        var services = new ServiceCollection();
        Func<Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription, bool> include = _ => false;

        services.AddHeadlessOpenApi(configure: options => options.ShouldInclude = include);
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<OpenApiOptions>>().Get("v1");
        options.ShouldInclude.Should().BeSameAs(include);
    }
}
