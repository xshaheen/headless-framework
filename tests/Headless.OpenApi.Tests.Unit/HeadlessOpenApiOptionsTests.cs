// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using FluentValidation;
using FluentValidation.Validators;
using Headless.Api;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Tests;

public sealed class HeadlessOpenApiOptionsTests : TestBase
{
    [Fact]
    public async Task should_publish_configured_schemes_and_let_the_selector_choose_per_operation()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                SampleApi.AddServices(builder);
                builder.Services.AddHeadlessOpenApi();
                builder.Services.ConfigureHeadlessOpenApi(options =>
                {
                    options.AddBearerSecurity = false;
                    options.AddApiKeySecurity = true;
                    options.ApiKeyHeaderName = "X-Workspace-Key";
                    options.SecuritySchemes["TenantBearer"] = new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "bearer",
                        BearerFormat = "JWT",
                    };
                    options.SecuritySchemeSelector = description =>
                        description.ActionDescriptor.EndpointMetadata.OfType<RequireTenantAttribute>().Any()
                            ? ["TenantBearer", HeadlessOpenApiOptions.ApiKeySchemeName]
                            : ["TenantBearer"];
                });
            },
            SampleApi.Map,
            "v1",
            AbortToken
        );

        var schemes = document["components"]!["securitySchemes"]!.AsObject();
        schemes.Select(x => x.Key).Should().BeEquivalentTo(["ApiKey", "TenantBearer"]);
        ((string?)schemes["ApiKey"]!["name"]).Should().Be("X-Workspace-Key");
        ((string?)schemes["ApiKey"]!["in"]).Should().Be("header");

        document.Operation("/tenant/orders", "get")["security"]!
            .ToJsonString()
            .Should()
            .Be("""[{"TenantBearer":[]},{"ApiKey":[]}]""");
        document.Operation("/orders/{id}", "get")["security"]!.ToJsonString().Should().Be("""[{"TenantBearer":[]}]""");
    }

    [Fact]
    public async Task should_fail_generation_when_the_selector_names_an_unknown_scheme()
    {
        var act = () =>
            OpenApiTestHost.GetDocumentAsync(
                builder =>
                {
                    builder.Services.AddHeadlessOpenApi();
                    builder.Services.ConfigureHeadlessOpenApi(
                        (options, _) => options.SecuritySchemeSelector = _ => ["Missing"]
                    );
                },
                app => app.MapGet("/secure", () => "secure").RequireAuthorization(),
                "v1",
                AbortToken
            );

        // The test server surfaces the generation failure on the request.
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*'Missing'*");
    }

    [Fact]
    public async Task should_apply_a_replacement_fluent_validation_rule()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                SampleApi.AddServices(builder);
                builder.Services.AddHeadlessOpenApi();
                builder.Services.ConfigureHeadlessOpenApi(options =>
                    options.FluentValidationRules.Add(
                        new FluentValidationRule
                        {
                            RuleName = FluentValidationRule.EmailRule.RuleName,
                            Matches = validator => validator is IEmailValidator,
                            Apply = context => context.PropertySchema!.Format = "email",
                        }
                    )
                );
            },
            SampleApi.Map,
            "v1",
            AbortToken
        );

        var email = document.Schema("CreateOrderRequest")["properties"]!["email"]!;
        ((string?)email["format"]).Should().Be("email");
        email["pattern"].Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task should_skip_a_failing_rule_unless_configured_to_throw(bool throwOnError)
    {
        var generate = () =>
            OpenApiTestHost.GetDocumentAsync(
                builder =>
                {
                    SampleApi.AddServices(builder);
                    builder.Services.AddHeadlessOpenApi();
                    builder.Services.ConfigureHeadlessOpenApi(options =>
                    {
                        options.ThrowOnSchemaProcessingError = throwOnError;
                        options.FluentValidationRules.Add(
                            new FluentValidationRule
                            {
                                RuleName = "Broken",
                                Matches = validator => validator is ILengthValidator,
                                Apply = _ => throw new InvalidOperationException("broken rule"),
                            }
                        );
                    });
                },
                SampleApi.Map,
                "v1",
                AbortToken
            );

        if (throwOnError)
        {
            await generate.Should().ThrowAsync<InvalidOperationException>().WithMessage("broken rule");

            return;
        }

        var document = await generate();
        ((int?)document.Schema("CreateOrderRequest")["properties"]!["name"]!["maxLength"]).Should().Be(10);
    }

    [Fact]
    public async Task should_apply_the_transformers_to_a_natively_registered_document()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                SampleApi.AddServices(builder);
                builder.Services.AddOpenApi("internal", options => options.AddHeadlessTransformers());
            },
            SampleApi.Map,
            "internal",
            AbortToken
        );

        ((string?)document.Operation("/api/products/{id}", "get")["operationId"]).Should().Be("Products_Get");
        document.Schema("OrderView")["properties"]!["count"]!.Types().Should().Equal("integer");
    }

    [Fact]
    public async Task should_publish_one_filtered_document_per_surface_with_the_surface_callback()
    {
        var documents = await OpenApiTestHost.GetDocumentsAsync(
            builder =>
            {
                builder.Services.AddHeadlessApiSurfaces(surfaces =>
                    surfaces.Add("portal", portal => portal.OpenApi.Title = "Portal API").Add("console")
                );
                builder.Services.AddHeadlessApiSurfaceDocuments(
                    configure: (options, surface) =>
                        options.AddDocumentTransformer(
                            (document, _, _) =>
                            {
                                document.Info.Description = $"{surface.SurfaceName} description";
                                document.Servers = [new OpenApiServer { Url = "https://api.example.com" }];
                                return Task.CompletedTask;
                            }
                        )
                );
            },
            app =>
            {
                app.MapGet("/portal/orders", () => TypedResults.Ok(new OrderView()))
                    .WithMetadata(new TestSurfaceMetadata("portal"))
                    .RequireAuthorization("orders.read");
                app.MapGet("/console/products", () => TypedResults.Ok(new ProductView { Name = "x" }))
                    .WithMetadata(new TestSurfaceMetadata("console"));
            },
            ["portal", "console"],
            AbortToken
        );

        foreach (var (name, surfaceDocument) in documents)
        {
            surfaceDocument.ShouldBeValidOpenApi($"the {name} surface document");
        }

        var portal = documents["portal"];
        ((string?)portal["info"]!["title"]).Should().Be("Portal API");
        ((string?)portal["info"]!["description"]).Should().Be("portal description");
        ((string?)portal["servers"]![0]!["url"]).Should().Be("https://api.example.com");
        portal["paths"]!.AsObject().Select(x => x.Key).Should().Equal("/portal/orders");
        portal.ToJsonString().Should().NotContain("ProductView");
        portal.Schema("OrderView")["properties"]!["count"]!.Types().Should().Equal("integer");
        _ResponseCodes(portal.Operation("/portal/orders", "get")).Should().Contain(["401", "403"]);

        var console = documents["console"];
        ((string?)console["info"]!["description"]).Should().Be("console description");
        console["paths"]!.AsObject().Select(x => x.Key).Should().Equal("/console/products");
        console.ToJsonString().Should().NotContain("OrderView");
    }

    [Fact]
    public async Task should_add_the_transformers_once_when_added_again()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                SampleApi.AddServices(builder);
                builder.Services.AddHeadlessOpenApi(configure: options => options.AddHeadlessTransformers());
            },
            SampleApi.Map,
            "v1",
            AbortToken
        );

        document.Operation("/orders/{id}", "get")["security"]!.ToJsonString().Should().Be("""[{"Bearer":[]}]""");
        ((string?)document.Operation("/api/products/{id}", "get")["operationId"]).Should().Be("Products_Get");
        document.Operation("/orders/{id}", "put")["parameters"]!.AsArray().Should().HaveCount(2);
        document.Schema("OrderView")["properties"]!["price"]!.Types().Should().Equal("number");
    }

    [Fact]
    public void should_reject_invalid_options_at_startup()
    {
        var services = new ServiceCollection();
        services.AddHeadlessOpenApi();
        services.ConfigureHeadlessOpenApi(options =>
        {
            options.AddApiKeySecurity = true;
            options.ApiKeyHeaderName = "";
            options.SecuritySchemes[HeadlessOpenApiOptions.BearerSchemeName] = new OpenApiSecurityScheme();
            options.SecuritySchemes[" "] = new OpenApiSecurityScheme();
        });
        using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IOptions<HeadlessOpenApiOptions>>().Value;

        act.Should()
            .Throw<OptionsValidationException>()
            .Which.Failures.Should()
            .Contain(failure => failure.Contains("ApiKeyHeaderName", StringComparison.Ordinal))
            .And.Contain(failure => failure.Contains("built-in bearer", StringComparison.Ordinal))
            .And.Contain(failure => failure.Contains("must not be empty", StringComparison.Ordinal));
    }

    [Fact]
    public void should_accept_the_default_options()
    {
        var services = new ServiceCollection();
        services.AddHeadlessOpenApi();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<HeadlessOpenApiOptions>>().Value;

        options.AddBearerSecurity.Should().BeTrue();
        options.AddApiKeySecurity.Should().BeFalse();
        options.ApiKeyHeaderName.Should().Be("X-Api-Key");
    }

    [Fact]
    public void should_describe_the_built_in_security_schemes()
    {
        var options = new HeadlessOpenApiOptions { AddApiKeySecurity = true, ApiKeyHeaderName = "X-Workspace-Key" };

        var schemes = options.GetSecuritySchemes();

        var bearer = schemes[HeadlessOpenApiOptions.BearerSchemeName];
        bearer.Type.Should().Be(SecuritySchemeType.Http);
        bearer.Scheme.Should().Be("bearer");
        bearer.BearerFormat.Should().Be("JWT");
        bearer
            .Description.Should()
            .Be("JWT Authorization header using the Bearer scheme.\nEnter Token without any prefix.");
        var apiKey = schemes[HeadlessOpenApiOptions.ApiKeySchemeName];
        apiKey.Type.Should().Be(SecuritySchemeType.ApiKey);
        apiKey.Name.Should().Be("X-Workspace-Key");
        apiKey.In.Should().Be(ParameterLocation.Header);
        apiKey.Description.Should().Be("API Key authentication using the X-Workspace-Key header.");
    }

    [Fact]
    public void should_build_the_schemes_and_merged_rules_once()
    {
        var options = new HeadlessOpenApiOptions();
        options.FluentValidationRules.Add(
            new FluentValidationRule
            {
                RuleName = "Custom",
                Matches = _ => false,
                Apply = _ => { },
            }
        );

        options.GetSecuritySchemes().Should().BeSameAs(options.GetSecuritySchemes());
        options.GetFluentValidationRules().Should().BeSameAs(options.GetFluentValidationRules());
        options.GetFluentValidationRules().Should().HaveCount(FluentValidationRule.DefaultRules.Length + 1);
    }

    [Fact]
    public void should_use_the_default_rules_as_is_when_none_are_added()
    {
        new HeadlessOpenApiOptions().GetFluentValidationRules().Should().BeSameAs(FluentValidationRule.DefaultRules);
    }

    [Fact]
    public void should_reject_a_custom_scheme_named_like_the_enabled_api_key_scheme()
    {
        var options = new HeadlessOpenApiOptions { AddApiKeySecurity = true };
        options.SecuritySchemes[HeadlessOpenApiOptions.ApiKeySchemeName] = new OpenApiSecurityScheme();

        var result = new HeadlessOpenApiOptionsValidator().Validate(options);

        result.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Contain("built-in API-key scheme");
    }

    [Fact]
    public void should_reject_a_null_fluent_validation_rule()
    {
        var options = new HeadlessOpenApiOptions();
        options.FluentValidationRules.Add(null!);

        var result = new HeadlessOpenApiOptionsValidator().Validate(options);

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be("FluentValidationRules[0]");
    }

    private static string[] _ResponseCodes(JsonNode operation) => operation["responses"]!.ExactStatusCodes();

    private sealed record TestSurfaceMetadata(string SurfaceName) : IApiSurfaceMetadata;
}
