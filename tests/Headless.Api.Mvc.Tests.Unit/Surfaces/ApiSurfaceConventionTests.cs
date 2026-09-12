// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Headless.Api.MultiTenancy;
using Headless.Api.Mvc.Surfaces;
using Headless.Api.Surfaces;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;

namespace Tests.Surfaces;

public sealed class ApiSurfaceConventionTests : TestBase
{
    [ApiSurface("Portal")]
    [Route("items")]
    private sealed class DummyPortalController
    {
        [HttpGet("{id}")]
        public IActionResult Get(string id) => new OkResult();
    }

    [Fact]
    public void should_apply_prefix_policy_and_tenancy_without_replacing_version_group()
    {
        var surfaceOptions = new ApiSurfaceOptions();
        surfaceOptions.AddSurface(
            "Portal",
            s =>
            {
                s.RoutePrefix = "api/portal";
                s.AuthorizationPolicy = "PortalUser";
                s.TenancyMode = ApiSurfaceTenancyMode.RequireTenant;
            }
        );

        var options = Options.Create(surfaceOptions);
        var convention = new ApiSurfaceConvention(new ApiSurfaceRegistry(options));

        var controllerType = typeof(DummyPortalController).GetTypeInfo();
        var controllerModel = new ControllerModel(controllerType, [new ApiSurfaceAttribute("Portal")]);
        controllerModel.ApiExplorer.GroupName = "v1";

        var actionMethod = controllerType.GetMethod(nameof(DummyPortalController.Get))!;
        var actionModel = new ActionModel(actionMethod, []) { Controller = controllerModel };

        var selector = new SelectorModel { AttributeRouteModel = new AttributeRouteModel(new RouteAttribute("items")) };
        controllerModel.Selectors.Add(selector);

        var actionSelector = new SelectorModel();
        actionModel.Selectors.Add(actionSelector);
        controllerModel.Actions.Add(actionModel);

        var appModel = new ApplicationModel();
        appModel.Controllers.Add(controllerModel);

        convention.Apply(appModel);

        controllerModel.ApiExplorer.GroupName.Should().Be("v1");
        controllerModel.Selectors[0].AttributeRouteModel!.Template.Should().Be("api/portal/items");

        actionSelector
            .EndpointMetadata.OfType<AuthorizeAttribute>()
            .Should()
            .ContainSingle(a => a.Policy == "PortalUser");
        actionSelector.EndpointMetadata.OfType<RequireTenantAttribute>().Should().ContainSingle();
    }

    [Fact]
    public void should_reject_unknown_surface_instead_of_skipping_authorization()
    {
        var convention = new ApiSurfaceConvention(new ApiSurfaceRegistry(Options.Create(new ApiSurfaceOptions())));
        var application = new ApplicationModel();
        application.Controllers.Add(
            new ControllerModel(typeof(DummyPortalController).GetTypeInfo(), [new ApiSurfaceAttribute("typo")])
        );
        var act = () => convention.Apply(application);
        act.Should().Throw<InvalidOperationException>().WithMessage("*typo*");
    }
}
