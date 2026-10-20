// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.Api;
using Headless.Caching;
using Headless.MultiTenancy;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>Pins what pre-authentication tenant catalog resolution adds to the document.</summary>
public sealed class TenantResolutionDocumentTests : TestBase
{
    private const string _CacheControl =
        """{"Cache-Control":{"description":"no-store when the tenant catalog rejected the request.","schema":{"type":"string"}}}""";

    private const string _HeaderDescription =
        "The identifier of the tenant the request targets, resolved through the tenant catalog before authentication.";

    private static Task<JsonNode> _GetDocumentAsync(
        Action<HeadlessTenantCatalogResolutionBuilder> configureSources,
        bool detailedErrors = false
    )
    {
        return OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder.Services.AddHeadlessCaching(caching => caching.UseInMemory());
                builder.Services.AddStatusCodesRewriterMiddleware();
                builder.AddHeadlessTenancy(tenancy =>
                {
                    tenancy.Catalog(catalog =>
                        catalog
                            .Configure(options => options.DetailedResolutionErrors = detailedErrors)
                            .UseInMemory(options =>
                                options.Tenants.Add(new TenantInfo("ten_1", "acme", "Acme", isEnabled: true))
                            )
                    );
                    tenancy.Http(http => http.ResolveFromCatalog(configureSources));
                });
                builder.Services.AddHeadlessOpenApi();
            },
            app =>
            {
                app.MapGet("/catalog/orders", () => TypedResults.Ok("orders")).AllowAnonymous();
                app.MapGet("/catalog/tenant", () => TypedResults.Ok("tenant")).RequireTenant().RequireAuthorization();
                app.MapGet("/catalog/orders/{id:int}", (int id) => TypedResults.Ok(id)).AllowAnonymous();
                app.MapPost("/catalog/orders", (ItemRequest request) => TypedResults.Ok(request.Name))
                    .ProducesValidationProblem()
                    .AllowAnonymous();
                app.MapGet("/catalog/health", () => TypedResults.Ok("health"))
                    .WithMetadata(new SkipTenantResolutionAttribute())
                    .AllowAnonymous();
            },
            "v1",
            AbortToken,
            app =>
            {
                app.UseStatusCodesRewriter();
                app.UseHeadlessTenantCatalogResolution();
            }
        );
    }

    [Fact]
    public async Task should_document_the_tenant_header_as_optional_unless_the_endpoint_requires_a_tenant()
    {
        var document = await _GetDocumentAsync(sources => sources.AddHeaderSource());

        document.Operation("/catalog/orders", "get")["parameters"]!.ShouldBeJson(
            $$$"""[{"name":"X-Tenant","in":"header","description":"{{{_HeaderDescription}}}","schema":{"type":"string"}}]"""
        );
        document.Operation("/catalog/tenant", "get")["parameters"]!.ShouldBeJson(
            $$$"""[{"name":"X-Tenant","in":"header","description":"{{{_HeaderDescription}}}","required":true,"schema":{"type":"string"}}]"""
        );
    }

    [Fact]
    public async Task should_keep_every_tenant_header_optional_when_another_source_can_supply_the_tenant()
    {
        var document = await _GetDocumentAsync(sources =>
            sources.AddHeaderSource("X-Tenant").AddHeaderSource("X-Legacy-Tenant").AddHostSource("{tenant}.example.com")
        );

        document.Operation("/catalog/tenant", "get")["parameters"]!.ShouldBeJson(
            """[{"name":"X-Tenant","in":"header","description":"The identifier of the tenant the request targets, resolved through the tenant catalog before authentication. Send at most one of X-Tenant, X-Legacy-Tenant: a request that sends several is rejected with 400.","schema":{"type":"string"}},{"name":"X-Legacy-Tenant","in":"header","description":"The identifier of the tenant the request targets, resolved through the tenant catalog before authentication. Send at most one of X-Tenant, X-Legacy-Tenant: a request that sends several is rejected with 400.","schema":{"type":"string"}}]"""
        );
    }

    [Fact]
    public async Task should_document_the_collapsed_tenant_rejections()
    {
        var document = await _GetDocumentAsync(sources => sources.AddHeaderSource());
        document.ShouldBeValidOpenApi("the collapsed tenant rejections");
        var responses = document.Operation("/catalog/orders", "get")["responses"]!;

        responses.AsObject().Select(response => response.Key).Should().Equal("200", "404", "409", "422", "400");

        var badRequest = responses["400"]!;
        ((string?)badRequest["description"]).Should().Be("Bad Request - The tenant identifier is invalid.");
        badRequest["headers"]!.ShouldBeJson(_CacheControl);
        _Examples(badRequest)
            .ShouldBeJson(
                """{"tenantIdentifierInvalid":{"summary":"Tenant identifier invalid","value":{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"bad-request","status":400,"detail":"The tenant identifier is invalid.","instance":"/public/some-endpoint","traceId":"<trace-id>","buildNumber":"<version>","commitNumber":"<commit>","timestamp":"2024-01-01T12:00:00+00:00","error":{"code":"g:tenant_identifier_invalid","description":"The tenant identifier is invalid."}}}}"""
            );

        var notFound = responses["404"]!;
        // The standard 404 is already documented, so it keeps its description and the tenant example joins its own.
        ((string?)notFound["description"])
            .Should()
            .Be("Not Found - The requested resource does not exist.");
        notFound["headers"]!.ShouldBeJson(_CacheControl);
        _Examples(notFound)!
            ["tenantResolutionFailed"]
            .ShouldBeJson(
                """{"summary":"Tenant not resolved","value":{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.5","title":"entity-not-found","status":404,"detail":"The requested resource was not found.","instance":"/public/some-endpoint","traceId":"<trace-id>","buildNumber":"<version>","commitNumber":"<commit>","timestamp":"2024-01-01T12:00:00+00:00","error":{"code":"g:tenant_resolution_failed","description":"The requested tenant could not be resolved."}}}"""
            );
    }

    [Fact]
    public async Task should_document_the_detailed_tenant_rejections_beside_the_responses_the_endpoint_already_has()
    {
        var document = await _GetDocumentAsync(sources => sources.AddHeaderSource(), detailedErrors: true);
        document.ShouldBeValidOpenApi("the detailed tenant rejections");
        var responses = document.Operation("/catalog/tenant", "get")["responses"]!;

        responses
            .AsObject()
            .Select(response => response.Key)
            .Should()
            .Equal("200", "401", "403", "404", "409", "422", "400");
        _ExampleCodes(responses["404"]!).Should().Equal("notFound:", "tenantUnknown:g:tenant_unknown");
        // The 403 the authorization already documents keeps its examples beside the tenant ones.
        _ExampleCodes(responses["403"]!)
            .Should()
            .Equal(
                "forbidden:",
                "tenantRequired:g:tenant_required",
                "tenantDisabled:g:tenant_disabled",
                "tenantIdentifierMismatch:g:tenant_identifier_mismatch"
            );
        ((string?)responses["403"]!["description"]).Should().NotContain("tenant");
        responses["403"]!["headers"]!.ShouldBeJson(_CacheControl);
    }

    [Fact]
    public async Task should_keep_the_example_of_a_400_the_operation_already_documents_beside_the_tenant_example()
    {
        var document = await _GetDocumentAsync(sources => sources.AddHeaderSource());
        var badRequest = document.Operation("/catalog/orders/{id}", "get")["responses"]!["400"]!;

        // The binding failure documented the 400 first, so its description stays.
        ((string?)badRequest["description"])
            .Should()
            .Be(
                "Bad Request - The request could not be read: a parameter or the body is missing, malformed, or of the wrong type."
            );
        _ExampleCodes(badRequest).Should().Equal("badRequest:", "tenantIdentifierInvalid:g:tenant_identifier_invalid");
        ((string?)_Examples(badRequest)["badRequest"]!["value"]!["detail"])
            .Should()
            .Be("The request could not be read. Its body is empty or malformed.");
        badRequest["headers"]!.ShouldBeJson(_CacheControl);
    }

    [Fact]
    public async Task should_add_no_tenant_example_to_a_400_declared_as_a_validation_problem()
    {
        var document = await _GetDocumentAsync(sources => sources.AddHeaderSource());
        var badRequest = document.Operation("/catalog/orders", "post")["responses"]!["400"]!;
        var problem = badRequest["content"]!["application/problem+json"]!;

        ((string?)problem["schema"]!["$ref"]).Should().Be("#/components/schemas/BadRequestValidationProblemDetails");
        problem["examples"].Should().BeNull();
        ((string?)problem["example"]!["title"]).Should().Be("One or more validation errors occurred.");
    }

    [Fact]
    public async Task should_leave_an_endpoint_that_skips_tenant_resolution_alone()
    {
        var document = await _GetDocumentAsync(sources => sources.AddHeaderSource());
        var operation = document.Operation("/catalog/health", "get");

        operation["parameters"].Should().BeNull();
        operation["responses"]!.AsObject().Select(response => response.Key).Should().Equal("200", "404", "409", "422");
    }

    [Fact]
    public async Task should_add_a_server_per_tenant_host_template_without_wildcards()
    {
        var document = await _GetDocumentAsync(sources =>
            sources.AddHostSource("{tenant}.example.com").AddHostSource("*.{tenant}.example.dev")
        );

        document.ShouldBeValidOpenApi("the tenant servers");
        // The first server is the one the generator derives from the request.
        var servers = document["servers"]!.AsArray();
        servers.Should().HaveCount(2);
        servers[1]
            .ShouldBeJson(
                """{"url":"https://{tenant}.example.com","description":"A tenant's host. The tenant catalog resolves the tenant from the host name.","variables":{"tenant":{"default":"tenant","description":"The identifier of the tenant."}}}"""
            );
        // The host source reads no header.
        document.Operation("/catalog/orders", "get")["parameters"].Should().BeNull();
    }

    [Fact]
    public async Task should_document_no_tenant_contract_without_catalog_resolution()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder => builder.Services.AddHeadlessOpenApi(),
            app => app.MapGet("/catalog/orders", () => TypedResults.Ok("orders")).AllowAnonymous(),
            "v1",
            AbortToken
        );

        var operation = document.Operation("/catalog/orders", "get");
        operation["parameters"].Should().BeNull();
        operation["responses"]!.AsObject().Select(response => response.Key).Should().Equal("200", "404", "409", "422");
        document["servers"]!.AsArray().Should().ContainSingle();
    }

    private static JsonNode _Examples(JsonNode response) =>
        response["content"]!["application/problem+json"]!["examples"]!;

    private static IEnumerable<string> _ExampleCodes(JsonNode response) =>
        response["content"]!["application/problem+json"]!["examples"]!
            .AsObject()
            .Select(example => $"{example.Key}:{(string?)example.Value!["value"]!["error"]?["code"]}");
}
