// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text.Json.Nodes;
using FluentValidation;
using Headless.Api;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Pins the responses and parameters the server adds before a handler runs: binding failures, the body size limit, the
/// antiforgery token, the file rules of a form, and endpoints the current environment hides.
/// </summary>
public sealed class RequestContractDocumentTests : TestBase
{
    private const string _BindingFailure =
        "Bad Request - The request could not be read: a parameter or the body is missing, malformed, or of the wrong type.";

    [Fact]
    public async Task should_document_a_400_on_every_operation_that_binds_a_request_value()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder => builder.Services.AddHeadlessOpenApi(),
            app =>
            {
                app.MapGet("/items/{id:int}", (int id) => TypedResults.Ok(id)).AllowAnonymous();
                app.MapGet("/search", (string? term) => TypedResults.Ok(term)).AllowAnonymous();
                app.MapGet("/tagged", ([FromHeader(Name = "X-Tag")] string tag) => TypedResults.Ok(tag))
                    .AllowAnonymous();
                app.MapPost("/items", (ItemRequest request) => TypedResults.Ok(request.Name)).AllowAnonymous();
                app.MapGet("/static", () => TypedResults.Ok("static")).AllowAnonymous();
            },
            "v1",
            AbortToken
        );

        foreach (
            var (path, method) in new[]
            {
                ("/items/{id}", "get"),
                ("/search", "get"),
                ("/tagged", "get"),
                ("/items", "post"),
            }
        )
        {
            var badRequest = document.Operation(path, method)["responses"]!["400"]!;
            ((string?)badRequest["description"]).Should().Be(_BindingFailure, $"{method} {path} binds a value");
            _ProblemSchemaRef(badRequest).Should().Be("#/components/schemas/BadRequestProblemDetails");
        }

        // An operation that binds nothing can fail no binding, so it documents no 400.
        document.Operation("/static", "get")["responses"]!
            .AsObject()
            .Select(x => x.Key)
            .Should()
            .Equal("200", "404", "409", "422");
    }

    [Fact]
    public async Task should_document_a_413_on_operations_with_a_body_unless_the_endpoint_lifts_the_limit()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder => builder.Services.AddHeadlessOpenApi(),
            app =>
            {
                app.MapPost("/server-limit", (ItemRequest request) => TypedResults.Ok(request.Name)).AllowAnonymous();
                app.MapPost("/own-limit", (ItemRequest request) => TypedResults.Ok(request.Name))
                    .WithMetadata(new RequestSizeLimitAttribute(1024))
                    .AllowAnonymous();
                app.MapPost("/no-limit", (ItemRequest request) => TypedResults.Ok(request.Name))
                    .WithMetadata(new DisableRequestSizeLimitAttribute())
                    .AllowAnonymous();
                app.MapGet("/no-body/{id:int}", (int id) => TypedResults.Ok(id)).AllowAnonymous();
            },
            "v1",
            AbortToken
        );

        var serverLimit = document.Operation("/server-limit", "post")["responses"]!["413"]!;
        ((string?)serverLimit["description"])
            .Should()
            .Be("Payload Too Large - The request body is larger than the server accepts.");
        _ProblemSchemaRef(serverLimit).Should().Be("#/components/schemas/PayloadTooLargeProblemDetails");
        ((string?)document.Operation("/own-limit", "post")["responses"]!["413"]!["description"])
            .Should()
            .Be("Payload Too Large - The request body is larger than 1024 bytes.");
        document.Operation("/no-limit", "post")["responses"]!["413"].Should().BeNull();
        document.Operation("/no-body/{id}", "get")["responses"]!["413"].Should().BeNull();
    }

    [Fact]
    public async Task should_document_the_antiforgery_header_and_its_400_on_a_form_endpoint()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder.Services.AddHeadlessAntiforgery();
                builder.Services.AddHeadlessOpenApi();
            },
            app =>
            {
                app.MapPost("/uploads", (IFormFile file) => TypedResults.Ok(file.FileName)).AllowAnonymous();
                app.MapPost("/open-uploads", (IFormFile file) => TypedResults.Ok(file.FileName))
                    .DisableAntiforgery()
                    .AllowAnonymous();
            },
            "v1",
            AbortToken
        );

        document.ShouldBeValidOpenApi("the antiforgery contract");
        var operation = document.Operation("/uploads", "post");
        operation["parameters"]!.ShouldBeJson(
            """[{"name":"X-XSRF-TOKEN","in":"header","description":"The antiforgery request token. Send it in this header or in the __RequestVerificationToken form field.","schema":{"type":"string"}}]"""
        );
        ((string?)operation["responses"]!["400"]!["description"])
            .Should()
            .Be(
                "Bad Request - The request could not be read: a parameter or the body is missing, malformed, or of the wrong type; or the antiforgery token is missing or invalid."
            );

        // DisableAntiforgery() after the form parameter's metadata turns validation off.
        var open = document.Operation("/open-uploads", "post");
        open["parameters"].Should().BeNull();
        ((string?)open["responses"]!["400"]!["description"]).Should().Be(_BindingFailure);
    }

    [Fact]
    public async Task should_not_document_an_antiforgery_header_when_antiforgery_is_not_registered()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder => builder.Services.AddHeadlessOpenApi(),
            app => app.MapPost("/uploads", (IFormFile file) => TypedResults.Ok(file.FileName)).AllowAnonymous(),
            "v1",
            AbortToken
        );

        var operation = document.Operation("/uploads", "post");
        operation["parameters"].Should().BeNull();
        ((string?)operation["responses"]!["400"]!["description"]).Should().Be(_BindingFailure);
    }

    [Fact]
    public async Task should_describe_the_file_rules_of_a_form_as_part_encoding_and_description()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder.Services.AddScoped<IValidator<AvatarForm>, AvatarFormValidator>();
                builder.Services.AddHeadlessOpenApi();
            },
            app =>
                app.MapPost("/avatars", ([FromForm] AvatarForm form) => TypedResults.Ok(form.Caption))
                    .DisableAntiforgery()
                    .AllowAnonymous(),
            "v1",
            AbortToken
        );

        document.ShouldBeValidOpenApi("the file rules");
        var multipart = document.Operation("/avatars", "post")["requestBody"]!["content"]!["multipart/form-data"]!;
        multipart["encoding"]!.ShouldBeJson(
            """{"image":{"contentType":"image/png, image/jpeg"},"thumbnail":{"contentType":"image/png"}}"""
        );

        // The part's own node takes the text, so the shared IFormFile component is left as it is.
        var properties = document.Schema("AvatarForm")["properties"]!;
        properties["image"]!.ShouldBeJson(
            """{"description":"Accepted content types: image/png, image/jpeg. Size: 1 to 1048576 bytes.","$ref":"#/components/schemas/IFormFile"}"""
        );
        // Two content-type rules both apply, so only the type they share is accepted.
        ((string?)properties["thumbnail"]!["description"])
            .Should()
            .Be("Accepted content types: image/png. Size: at most 2048 bytes.");
        ((string?)properties["attachment"]!["description"]).Should().Be("Must not be empty.");
        ((string?)properties["document"]!["description"]).Should().Be("Size: at least 512 bytes.");
        document.Schema("IFormFile").ShouldBeJson("""{"type":"string","format":"binary"}""");
    }

    [Fact]
    public async Task should_describe_the_file_rules_of_an_mvc_form_model_on_its_bound_properties()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder.Services.AddScoped<IValidator<AvatarForm>, AvatarFormValidator>();
                builder
                    .Services.AddControllers()
                    .ConfigureApplicationPartManager(manager =>
                        manager.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(AvatarsController)))
                    );
                builder.Services.AddHeadlessOpenApi();
            },
            app => app.MapControllers(),
            "v1",
            AbortToken
        );

        // MVC binds each form property as its own parameter, so the rules come from the declaring type's validator.
        var schema = document.Operation("/mvc/avatars", "post")["requestBody"]!["content"]!.AsObject().Single().Value![
            "schema"
        ]!;
        ((string?)schema["properties"]!["Image"]!["description"])
            .Should()
            .Be("Accepted content types: image/png, image/jpeg. Size: 1 to 1048576 bytes.");
        schema["properties"]!["Caption"]!["description"].Should().BeNull();
    }

    [Fact]
    public async Task should_leave_out_endpoints_the_current_environment_hides()
    {
        // The test host runs in the Production environment.
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder => builder.Services.AddHeadlessOpenApi(),
            app =>
            {
                app.MapGet("/diagnostics", () => TypedResults.Ok("dev"))
                    .WithMetadata(new RequireEnvironmentAttribute("Development"))
                    .AllowAnonymous();
                app.MapGet("/status", () => TypedResults.Ok("prod"))
                    .WithMetadata(new RequireEnvironmentAttribute("Production"))
                    .AllowAnonymous();
                app.MapGet("/seed", () => TypedResults.Ok("seed"))
                    .WithMetadata(new BlockInEnvironmentAttribute("Production"))
                    .AllowAnonymous();
                app.MapGet("/debug", () => TypedResults.Ok("debug"))
                    .WithMetadata(new BlockInEnvironmentAttribute("Development"))
                    .AllowAnonymous();
            },
            "v1",
            AbortToken
        );

        document["paths"]!.AsObject().Select(path => path.Key).Should().BeEquivalentTo(["/status", "/debug"]);
    }

    private static string? _ProblemSchemaRef(JsonNode response) =>
        (string?)response["content"]?["application/problem+json"]?["schema"]?["$ref"];
}

public sealed record ItemRequest(string Name);

public sealed class AvatarForm
{
    public IFormFile? Image { get; set; }

    public IFormFile? Thumbnail { get; set; }

    public IFormFile? Attachment { get; set; }

    public IFormFile? Document { get; set; }

    public string? Caption { get; set; }
}

public sealed class AvatarFormValidator : AbstractValidator<AvatarForm>
{
    public AvatarFormValidator()
    {
        RuleFor(x => x.Image)
            .NotNull()
            .FileNotEmpty()
            .LessThanOrEqualTo(1048576)
            .ContentTypes(["image/png", "image/jpeg"]);
        RuleFor(x => x.Thumbnail)
            .ContentTypes(["image/png", "image/gif"])
            .ContentTypes(["image/png", "image/jpeg"])
            .LessThanOrEqualTo(4096)
            .LessThanOrEqualTo(2048);
        RuleFor(x => x.Attachment).FileNotEmpty();
        RuleFor(x => x.Document).GreaterThanOrEqualTo(512);
        // A conditional rule may not run, so it documents nothing.
        RuleFor(x => x.Attachment).ContentTypes(["application/pdf"]).When(x => x.Caption is not null);
    }
}

[NonController]
[ApiController]
[Route("mvc/avatars")]
public sealed class AvatarsController : ControllerBase
{
    [HttpPost]
    public string Post([FromForm] AvatarForm form) => form.Caption ?? "";
}

/// <summary>Adds one controller that the default provider skips, so it stays out of the other test documents.</summary>
internal sealed class SingleControllerFeatureProvider(Type controller) : IApplicationFeatureProvider<ControllerFeature>
{
    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        feature.Controllers.Add(controller.GetTypeInfo());
    }
}
