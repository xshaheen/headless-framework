// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Api;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.Surfaces;

public sealed class ApiSurfaceDocumentInferenceTests : TestBase
{
    [Fact]
    public void should_return_the_registered_document_names_and_close_surface_registration()
    {
        var services = new ServiceCollection()
            .AddHeadlessApiSurface("portal")
            .AddHeadlessApiSurface("console", surface => surface.OpenApi.DocumentName = "admin");

        var names = services.InferHeadlessApiSurfaceDocumentNames();
        var register = () => services.AddHeadlessApiSurface("partner");

        names.Should().Equal("portal", "admin");
        services.InferHeadlessApiSurfaceDocumentNames().Should().Equal("portal", "admin");
        register.Should().Throw<InvalidOperationException>().WithMessage("*before OpenAPI document inference*");
    }
}
