// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Api.Surfaces;
using Headless.OpenApi.Nswag;
using Headless.OpenApi.Nswag.Surfaces;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using NSwag.AspNetCore;

namespace Tests.Surfaces;

public sealed class NswagSurfaceGenerationTests : TestBase
{
    [Fact]
    public void should_register_nswag_documents_for_configured_surfaces()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddControllers();

        services.AddHeadlessApiSurfaces(options =>
        {
            options.AddSurface(
                "Portal",
                s =>
                {
                    s.RoutePrefix = "api/portal";
                    s.GroupName = "PortalGroup";
                    s.DocumentName = "portal";
                    s.DocumentTitle = "Portal API";
                    s.TenancyPosture = SurfaceTenancyPosture.RequireTenant;
                }
            );
            options.AddSurface(
                "Console",
                s =>
                {
                    s.RoutePrefix = "api/console";
                    s.GroupName = "ConsoleGroup";
                    s.DocumentName = "console";
                    s.DocumentTitle = "Console API";
                    s.TenancyPosture = SurfaceTenancyPosture.SkipTenantResolution;
                }
            );
        });

        services.AddNswagOpenApiSurfaces();

        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetServices<OpenApiDocumentRegistration>().ToList();

        registrations.Should().HaveCount(2);

        var portalReg = registrations.Single(r => r.DocumentName == "portal");
        portalReg.Settings.Title.Should().Be("Portal API");
        portalReg.Settings.ApiGroupNames.Should().ContainSingle(g => g == "PortalGroup");
        portalReg
            .Settings.OperationProcessors.OfType<TenantRequiredForbiddenExampleOperationProcessor>()
            .Should()
            .ContainSingle();

        var consoleReg = registrations.Single(r => r.DocumentName == "console");
        consoleReg.Settings.Title.Should().Be("Console API");
        consoleReg.Settings.ApiGroupNames.Should().ContainSingle(g => g == "ConsoleGroup");
        consoleReg
            .Settings.OperationProcessors.OfType<TenantRequiredForbiddenExampleOperationProcessor>()
            .Should()
            .BeEmpty();
    }
}
