// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Tests;

public sealed class TusCorsDefaultsTests : TestBase
{
    [Fact]
    public void should_cover_the_tus_response_surface_when_exposed_headers()
    {
        // A browser client cannot resume without reading these cross-origin
        TusCorsDefaults
            .ExposedHeaders.Should()
            .Contain([
                "Location",
                "Tus-Resumable",
                "Upload-Offset",
                "Upload-Length",
                "Upload-Defer-Length",
                "Upload-Expires",
                "Upload-Metadata",
            ]);
    }

    [Fact]
    public void should_be_a_superset_of_tusdotnet_cors_helper_when_exposed_headers()
    {
        // tusdotnet ships CorsHelper.GetExposedHeaders(); ours must never expose less than it
        TusCorsDefaults.ExposedHeaders.Should().Contain(tusdotnet.Helpers.CorsHelper.GetExposedHeaders());
    }

    [Fact]
    public void should_cover_the_tus_request_surface_when_allowed_headers()
    {
        TusCorsDefaults
            .AllowedHeaders.Should()
            .Contain(["Tus-Resumable", "Upload-Length", "Upload-Metadata", "Upload-Checksum", "Content-Type"]);
    }

    [Fact]
    public void should_include_patch_and_delete_when_allowed_methods()
    {
        // PATCH (append) and DELETE (termination) are the ones default CORS configs miss
        TusCorsDefaults.AllowedMethods.Should().Contain(["PATCH", "DELETE", "HEAD", "POST", "OPTIONS", "GET"]);
    }

    [Fact]
    public void should_apply_headers_and_methods_to_the_policy_when_with_tus_headers()
    {
        // given
        var builder = new CorsPolicyBuilder().WithOrigins("https://app.example.com");

        // when
        var policy = builder.WithTusHeaders().Build();

        // then
        policy.Headers.Should().Contain(TusCorsDefaults.AllowedHeaders);
        policy.ExposedHeaders.Should().Contain(TusCorsDefaults.ExposedHeaders);
        policy.Methods.Should().Contain(TusCorsDefaults.AllowedMethods);
        policy.Origins.Should().ContainSingle().Which.Should().Be("https://app.example.com");
    }
}
