// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.Surfaces;
using Headless.OpenApi.Nswag;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using NSwag.AspNetCore;

namespace Tests.Surfaces;

public sealed class NswagSurfaceGenerationTests : TestBase
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void should_register_inferred_or_explicit_documents_alongside_native_documents(bool infer)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();
        var configureCalls = 0;
        if (!infer)
        {
            services.AddNswagOpenApiSurfaces(["portal", "console"]);
        }

        services.AddNswagOpenApi(setupGeneratorActions: settings => settings.DocumentName = "extra");
        services.AddOpenApiDocument(settings => settings.DocumentName = "native");
        services.AddHeadlessApiSurfaces(options =>
        {
            configureCalls++;
            options.AddSurface("Portal");
        });
        services.AddHeadlessApiSurfaces(options => options.AddSurface("Console"));
        if (infer)
        {
            services.AddNswagOpenApiSurfaces();
        }

        configureCalls.Should().Be(1);
        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetServices<OpenApiDocumentRegistration>().ToArray();
        registrations.Select(x => x.DocumentName).Should().BeEquivalentTo("portal", "console", "extra", "native");
        configureCalls.Should().Be(1);
        provider.GetRequiredService<ApiSurfaceRegistry>().Surfaces.Should().HaveCount(2);
        registrations.Single(x => x.DocumentName == "portal").Settings.Title.Should().Be("Portal API");
    }

    [Fact]
    public void should_reject_surfaces_registered_after_inference()
    {
        var services = new ServiceCollection();
        services.AddNswagOpenApiSurfaces();
        var register = () => services.AddHeadlessApiSurfaces(options => options.AddSurface("portal"));
        register.Should().Throw<InvalidOperationException>().WithMessage("*before OpenAPI document inference*");
    }
}
