// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using Headless.Api;
using Headless.IO;
using Headless.OpenApi;
using Headless.Primitives;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Pins how a request body describes an <see cref="IUploadedFile"/> part and a collection of Headless primitives: an
/// upload is a binary part of a multipart body, as an <see cref="IFormFile"/> is, and a collection's elements keep the
/// primitive's underlying type.
/// </summary>
public sealed class UploadedFileDocumentTests : TestBase
{
    private static Task<JsonNode> _GetDocumentAsync()
    {
        return OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder
                    .Services.AddControllers()
                    .ConfigureApplicationPartManager(manager =>
                        manager.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(ProfilesController)))
                    );
                builder.Services.ConfigureHeadlessMvc();
                builder.Services.AddHeadlessOpenApi();
            },
            app =>
            {
                app.MapControllers();
                app.MapPost("/roles", (AssignRoles body) => TypedResults.Ok(body.RoleIds.Count)).AllowAnonymous();
            },
            "v1",
            AbortToken
        );
    }

    [Fact]
    public async Task should_describe_an_uploaded_file_as_a_binary_part_of_a_multipart_body()
    {
        var document = await _GetDocumentAsync();

        document.ShouldBeValidOpenApi("an uploaded-file form");
        var content = document.Operation("/profiles/avatar", "put")["requestBody"]!["content"]!.AsObject();
        content.Select(x => x.Key).Should().Equal("multipart/form-data");
        var avatar = content["multipart/form-data"]!["schema"]!["properties"]!["Avatar"]!;
        ((string?)avatar["$ref"]).Should().Be("#/components/schemas/IUploadedFile");
        document.Schema("IUploadedFile").ShouldBeJson("""{"type":"string","format":"binary"}""");
    }

    [Fact]
    public async Task should_keep_a_form_without_an_uploaded_file_url_encoded()
    {
        var document = await _GetDocumentAsync();

        var content = document.Operation("/profiles/name", "put")["requestBody"]!["content"]!.AsObject();
        content.Select(x => x.Key).Should().Equal("application/x-www-form-urlencoded");
    }

    [Fact]
    public async Task should_describe_the_elements_of_a_primitive_collection_as_the_underlying_type()
    {
        var document = await _GetDocumentAsync();

        document.ShouldBeValidOpenApi("a primitive collection");
        document.Schema("AssignRoles")["properties"]!["roleIds"]!["items"].ShouldBeJson("""{"type":"string"}""");
    }
}

public sealed record AssignRoles(IReadOnlyList<UserId> RoleIds);

public sealed class UpdateAvatarForm
{
    public IUploadedFile? Avatar { get; init; }

    public string? Caption { get; init; }
}

public sealed class UpdateNameForm
{
    public string? Name { get; init; }
}

[NonController]
[ApiController]
[AllowAnonymous]
[Route("profiles")]
public sealed class ProfilesController : ControllerBase
{
    [HttpPut("avatar")]
    public IActionResult UpdateAvatar([FromForm] UpdateAvatarForm form) => Ok(form.Avatar?.FileName);

    [HttpPut("name")]
    public IActionResult UpdateName([FromForm] UpdateNameForm form) => Ok(form.Name);
}
