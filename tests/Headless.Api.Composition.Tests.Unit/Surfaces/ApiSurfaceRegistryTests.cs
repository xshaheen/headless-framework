// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.Surfaces;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tests.Surfaces;

public sealed class ApiSurfaceRegistryTests : TestBase
{
    [Fact]
    public void should_freeze_configuration_once_per_host()
    {
        ApiSurfaceBuilder? configured = null;
        var services = new ServiceCollection();
        services.AddHeadlessApiSurfaces(options =>
            options.AddSurface(
                "portal",
                surface =>
                {
                    configured = surface;
                    surface.RoutePrefix = "/api/portal/";
                }
            )
        );
        using var firstHost = services.BuildServiceProvider();
        var registry = firstHost.GetRequiredService<ApiSurfaceRegistry>();
        configured!.RoutePrefix = "changed";
        configured.OpenApi.Title = "changed";
        registry.GetRequiredSurface("PORTAL").RoutePrefix.Should().Be("api/portal");
        registry.GetRequiredSurface("portal").OpenApi.Title.Should().Be("portal API");
        firstHost.GetRequiredService<ApiSurfaceRegistry>().Should().BeSameAs(registry);
        using var secondHost = services.BuildServiceProvider();
        secondHost.GetRequiredService<ApiSurfaceRegistry>().Should().NotBeSameAs(registry);
    }

    [Theory]
    [InlineData("unknown", "portal", 0)]
    [InlineData("unclassified", "portal", 0)]
    [InlineData("portal", "../portal", 0)]
    [InlineData("portal", "portal", 99)]
    public void should_reject_invalid_configuration(string name, string document, int tenancyMode)
    {
        var services = new ServiceCollection();
        services.AddHeadlessApiSurfaces(options =>
            options.AddSurface(
                name,
                surface =>
                {
                    surface.OpenApi.DocumentName = document;
                    surface.TenancyMode = (ApiSurfaceTenancyMode)tenancyMode;
                }
            )
        );
        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<ApiSurfaceRegistry>();
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void should_reject_duplicate_document_names()
    {
        var services = new ServiceCollection();
        services.AddHeadlessApiSurfaces(options =>
        {
            options.AddSurface("portal");
            options.AddSurface("console", surface => surface.OpenApi.DocumentName = "PORTAL");
        });
        using var provider = services.BuildServiceProvider();
        var act = () => provider.GetRequiredService<ApiSurfaceRegistry>();
        act.Should().Throw<OptionsValidationException>().WithMessage("*document names must be unique*");
    }

    [Fact]
    public void should_reject_duplicate_surface_names()
    {
        var options = new ApiSurfaceOptions().AddSurface("portal");
        var act = () => options.AddSurface("PORTAL");
        act.Should().Throw<InvalidOperationException>().WithMessage("*already configured*");
    }
}
