// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Testing.Tests;
using Headless.Tus;
using Microsoft.AspNetCore.Cors.Infrastructure;

namespace Tests;

public sealed class TusCorsDefaultsTests : TestBase
{
    [Fact]
    public void exposed_headers_should_cover_the_tus_response_surface()
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
    public void exposed_headers_should_be_a_superset_of_tusdotnet_cors_helper()
    {
        // tusdotnet ships CorsHelper.GetExposedHeaders(); ours must never expose less than it
        TusCorsDefaults.ExposedHeaders.Should().Contain(tusdotnet.Helpers.CorsHelper.GetExposedHeaders());
    }

    [Fact]
    public void allowed_headers_should_cover_the_tus_request_surface()
    {
        TusCorsDefaults
            .AllowedHeaders.Should()
            .Contain(["Tus-Resumable", "Upload-Length", "Upload-Metadata", "Upload-Checksum", "Content-Type"]);
    }

    [Fact]
    public void allowed_methods_should_include_patch_and_delete()
    {
        // PATCH (append) and DELETE (termination) are the ones default CORS configs miss
        TusCorsDefaults.AllowedMethods.Should().Contain(["PATCH", "DELETE", "HEAD", "POST", "OPTIONS", "GET"]);
    }

    [Fact]
    public void with_tus_headers_should_apply_headers_and_methods_to_the_policy()
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
