// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using FluentValidation;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// Pins that the parts of a form body carry the constraints and required-ness a JSON body gets from its validator and
/// from the nullability of its members.
/// </summary>
public sealed class FormPartDocumentTests : TestBase
{
    [Fact]
    public async Task should_require_an_mvc_form_file_part_that_a_validator_or_its_type_requires()
    {
        var document = await _GetMvcDocumentAsync();

        document.ShouldBeValidOpenApi("the MVC forms");
        var schema = _MultipartSchema(document, "/mvc/forms/avatar");
        schema.Required().Should().Equal("Avatar");
        // The file part stays a reference, so the rules leave the shared IFormFile component as it is.
        schema["properties"]!["Avatar"]!.ShouldBeJson("""{"$ref":"#/components/schemas/IFormFile"}""");
        document.Schema("IFormFile").ShouldBeJson("""{"type":"string","format":"binary"}""");
    }

    [Fact]
    public async Task should_apply_the_rules_of_the_model_and_its_nested_validators_to_dotted_mvc_form_parts()
    {
        var document = await _GetMvcDocumentAsync();

        var schema = _MultipartSchema(document, "/mvc/forms/profile");
        var properties = schema["properties"]!;

        // Name.First is nullable, but the model's validator requires it; Name.Last and Age are non-nullable.
        schema.Required().Should().BeEquivalentTo(["Name.First", "Name.Last", "Age", "Phone.Code", "Phone.Number"]);
        properties["Name.First"]!.ShouldBeJson("""{"type":"string","minLength":1,"maxLength":20}""");
        // The included validator and the one registered for the nested type constrain their parts too.
        properties["Bio"]!.ShouldBeJson("""{"type":"string","maxLength":100}""");
        ((int)properties["Name.Last"]!["maxLength"]!).Should().Be(50);
        ((int)properties["Age"]!["minimum"]!).Should().Be(1);
        ((int)properties["Age"]!["maximum"]!).Should().Be(120);
        // A validator attached with SetValidator constrains the parts of its nested object.
        properties["Phone.Code"]!.ShouldBeJson("""{"type":"string","minLength":1,"pattern":"^\\+[0-9]+$"}""");
        // An optional photo is never required, and the part below an optional object cannot be required in a flat form,
        // even though its own type and rule would make it so.
        schema.Required().Should().NotContain(["Photo", "Backup.Number"]);
        ((int)properties["Backup.Number"]!["minLength"]!).Should().Be(1);
    }

    [Fact]
    public async Task should_match_a_renamed_mvc_form_part_to_the_rules_of_its_declaring_type()
    {
        var document = await _GetMvcDocumentAsync();

        // A renamed part matches no property path, so the declaring type's own validator constrains it.
        var renamed = _MultipartSchema(document, "/mvc/forms/renamed");
        renamed.Required().Should().Equal("pic");
        ((int)renamed["properties"]!["note"]!["maxLength"]!).Should().Be(10);
    }

    [Fact]
    public async Task should_require_a_minimal_api_form_file_part_that_a_validator_requires()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder.Services.AddScoped<IValidator<UpdateAvatar>, UpdateAvatarValidator>();
                builder.Services.AddHeadlessOpenApi();
            },
            app =>
                app.MapPost("/avatars", ([FromForm] UpdateAvatar form) => TypedResults.Ok(form.Avatar.FileName))
                    .DisableAntiforgery()
                    .AllowAnonymous(),
            "v1",
            AbortToken
        );

        // A Minimal API form model is a component, which the schema transformers constrain like a JSON body.
        ((string?)_MultipartSchema(document, "/avatars")["$ref"])
            .Should()
            .Be("#/components/schemas/UpdateAvatar");
        document.Schema("UpdateAvatar").Required().Should().Equal("avatar");
    }

    private static Task<JsonNode> _GetMvcDocumentAsync()
    {
        return OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder.Services.AddScoped<IValidator<UpdateAvatar>, UpdateAvatarValidator>();
                builder.Services.AddScoped<IValidator<ProfileUpdate>, ProfileUpdateValidator>();
                builder.Services.AddScoped<IValidator<ProfileName>, ProfileNameValidator>();
                builder.Services.AddScoped<IValidator<RenamedUpload>, RenamedUploadValidator>();
                builder
                    .Services.AddControllers()
                    .ConfigureApplicationPartManager(manager =>
                        manager.FeatureProviders.Add(new SingleControllerFeatureProvider(typeof(FormsController)))
                    );
                builder.Services.AddHeadlessOpenApi();
            },
            app => app.MapControllers(),
            "v1",
            AbortToken
        );
    }

    private static JsonNode _MultipartSchema(JsonNode document, string path)
    {
        return document.Operation(path, "post")["requestBody"]!["content"]!["multipart/form-data"]!["schema"]!;
    }
}

public sealed record UpdateAvatar(IFormFile Avatar);

public sealed class UpdateAvatarValidator : AbstractValidator<UpdateAvatar>
{
    public UpdateAvatarValidator()
    {
        RuleFor(x => x.Avatar).NotNull();
    }
}

public sealed class ProfileName
{
    public string? First { get; set; }

    public string Last { get; set; } = "";
}

public sealed class ProfilePhone
{
    public string? Code { get; set; }

    public string? Number { get; set; }
}

public sealed class ProfilePhoneValidator : AbstractValidator<ProfilePhone>
{
    public ProfilePhoneValidator()
    {
        RuleFor(x => x.Code).NotEmpty().Matches(@"^\+[0-9]+$");
        RuleFor(x => x.Number).NotEmpty();
    }
}

public sealed class ProfileUpdate
{
    public ProfileName Name { get; set; } = new();

    public ProfilePhone Phone { get; set; } = new();

    public ProfilePhone? Backup { get; set; }

    public string? Bio { get; set; }

    public int Age { get; set; }

    public IFormFile? Photo { get; set; }
}

public sealed class ProfileUpdateValidator : AbstractValidator<ProfileUpdate>
{
    public ProfileUpdateValidator()
    {
        RuleFor(x => x.Name.First).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Phone).SetValidator(new ProfilePhoneValidator());
        RuleFor(x => x.Backup!).SetValidator(new ProfilePhoneValidator());
        RuleFor(x => x.Age).InclusiveBetween(1, 120);
        Include(new ProfileBioRules());
    }
}

public sealed class ProfileBioRules : AbstractValidator<ProfileUpdate>
{
    public ProfileBioRules()
    {
        RuleFor(x => x.Bio).MaximumLength(100);
    }
}

public sealed class ProfileNameValidator : AbstractValidator<ProfileName>
{
    public ProfileNameValidator()
    {
        RuleFor(x => x.Last).MaximumLength(50);
    }
}

public sealed class RenamedUpload
{
    [FromForm(Name = "pic")]
    public IFormFile? Picture { get; set; }

    [FromForm(Name = "note")]
    public string? Note { get; set; }
}

public sealed class RenamedUploadValidator : AbstractValidator<RenamedUpload>
{
    public RenamedUploadValidator()
    {
        RuleFor(x => x.Picture).NotNull();
        RuleFor(x => x.Note).MaximumLength(10);
    }
}

[NonController]
[ApiController]
[Route("mvc/forms")]
public sealed class FormsController : ControllerBase
{
    [HttpPost("avatar")]
    public string PostAvatar([FromForm] UpdateAvatar form) => form.Avatar.FileName;

    [HttpPost("renamed")]
    public string PostRenamed([FromForm] RenamedUpload form) => form.Note ?? "";

    [HttpPost("profile")]
    public string PostProfile([FromForm] ProfileUpdate form) => form.Bio ?? "";
}
