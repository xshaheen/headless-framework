// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentValidation;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Tests;

public sealed class NestedValidationDocumentTests : TestBase
{
    [Fact]
    public async Task should_characterize_property_schemas_before_component_extraction()
    {
        IOpenApiSchema? observed = null;
        var document = await _GetDocumentAsync(options =>
            options.AddSchemaTransformer(
                (schema, context, _) =>
                {
                    if (context.JsonTypeInfo.Type == typeof(NestedCustomer) && schema.Properties is { } properties)
                    {
                        observed = properties["name"];
                        ((OpenApiSchema)observed).MaxProperties = 7;
                    }

                    return Task.CompletedTask;
                }
            )
        );

        observed.Should().BeOfType<OpenApiSchema>();
        ((int)document.Schema("PersonName")["maxProperties"]!).Should().Be(7);
    }

    [Fact]
    public async Task should_publish_root_member_rules_as_a_use_site_overlay_without_constraining_the_shared_component()
    {
        var document = await _GetDocumentAsync();

        document.ShouldBeValidOpenApi("nested JSON validation rules");
        var name = document.Schema("NestedCustomer")["properties"]!["name"]!;
        name["allOf"]![0]!.ShouldBeJson("""{"$ref":"#/components/schemas/PersonName"}""");
        var overlay = name["allOf"]![1]!;
        overlay.Required().Should().Equal("first");
        overlay["properties"]!["first"]!.ShouldBeJson("""{"type":"string","minLength":1,"maxLength":20}""");
        overlay["properties"]!["family"]!["maxLength"]!.GetValue<int>().Should().Be(30);
        document.Schema("PersonName")["properties"]!["first"]!["maxLength"].Should().BeNull();
        document.Schema("PersonName").Required().Should().BeEmpty();
    }

    [Fact]
    public async Task should_leave_another_use_without_root_rules_as_a_plain_reference()
    {
        var document = await _GetDocumentAsync();

        document.Schema("PlainCustomer")["properties"]!["name"]!.ShouldBeJson(
            """{"$ref":"#/components/schemas/PersonName"}"""
        );
    }

    [Fact]
    public async Task should_keep_the_nullable_parent_outside_the_overlay()
    {
        var document = await _GetDocumentAsync();

        var name = document.Schema("NullableCustomer")["properties"]!["name"]!;
        var alternatives = name["oneOf"]!.AsArray();
        alternatives.Should().HaveCount(2);
        alternatives[1]!.ShouldBeJson("""{"type":"null"}""");
        var overlay = alternatives[0]!["allOf"]![1]!;
        overlay.Required().Should().Equal("first");
        overlay["properties"]!["first"]!.Types().Should().Equal("string");
        document.Schema("NullableCustomer").Required().Should().NotContain("name");
    }

    [Fact]
    public async Task should_nest_and_merge_two_level_member_paths_and_only_require_members_with_required_rules()
    {
        var document = await _GetDocumentAsync();

        var overlay = document.Schema("DeepCustomer")["properties"]!["address"]!["allOf"]![1]!;
        overlay.Required().Should().BeEmpty();
        var name = overlay["properties"]!["name"]!;
        name.Required().Should().Equal("first");
        name["properties"]!["first"]!.ShouldBeJson("""{"type":"string","maxLength":10}""");
        ((int)name["properties"]!["family"]!["maxLength"]!).Should().Be(40);
        document.Schema("CustomerAddress")["properties"]!["name"]!.ShouldBeJson(
            """{"$ref":"#/components/schemas/PersonName"}"""
        );
    }

    [Fact]
    public async Task should_publish_nested_rules_for_a_response_type()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            _ConfigureServices,
            app => app.MapGet("/customers/response", () => new NestedCustomer()).AllowAnonymous(),
            "v1",
            AbortToken
        );

        document.ShouldBeValidOpenApi("response nested validation rules");
        document.Operation("/customers/response", "get")["responses"]!["200"]!["content"]!["application/json"]![
            "schema"
        ]!.ShouldBeJson("""{"$ref":"#/components/schemas/NestedCustomer"}""");
        (
            (int)
                document.Schema("NestedCustomer")["properties"]!["name"]!["allOf"]![1]!["properties"]!["first"]![
                    "maxLength"
                ]!
        )
            .Should()
            .Be(20);
    }

    [Fact]
    public async Task should_overlay_a_component_once_when_several_operations_use_it()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            _ConfigureServices,
            app =>
            {
                app.MapPost("/customers/a", ([FromBody] NestedCustomer customer) => TypedResults.Ok(customer))
                    .AllowAnonymous();
                app.MapPut("/customers/b", ([FromBody] NestedCustomer customer) => TypedResults.Ok(customer))
                    .AllowAnonymous();
                app.MapGet("/customers/c", () => new NestedCustomer()).AllowAnonymous();
            },
            "v1",
            AbortToken
        );

        document.ShouldBeValidOpenApi("a component shared by several operations");
        var allOf = document.Schema("NestedCustomer")["properties"]!["name"]!["allOf"]!.AsArray();
        allOf.Should().HaveCount(2);
        allOf[0]!.ShouldBeJson("""{"$ref":"#/components/schemas/PersonName"}""");
    }

    [Fact]
    public async Task should_require_a_nullable_nested_object_and_skip_a_member_json_does_not_publish()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            _ConfigureServices,
            app =>
                app.MapPost("/optional", ([FromBody] OptionalLeafCustomer customer) => TypedResults.Ok(customer))
                    .AllowAnonymous(),
            "v1",
            AbortToken
        );

        document.ShouldBeValidOpenApi("a nullable nested leaf");
        var overlay = document.Schema("OptionalLeafCustomer")["properties"]!["address"]!["allOf"]![1]!;
        // NotNull on a nullable object leaf requires it and narrows its type to object, which excludes null; the rule on
        // the [JsonIgnore] member has no published property to land on, so the overlay leaves it out.
        overlay.ShouldBeJson("""{"required":["name"],"properties":{"name":{"type":"object"}}}""");
    }

    [Fact]
    public async Task should_fail_generation_on_a_nested_rule_that_throws_when_configured_to_throw()
    {
        var act = () =>
            OpenApiTestHost.GetDocumentAsync(
                builder =>
                {
                    builder.Services.AddScoped<IValidator<NestedCustomer>>(_ =>
                    {
                        // Only the dotted rule exists, so only the use-site overlay pass applies it.
                        var validator = new InlineValidator<NestedCustomer>();
                        validator.RuleFor(x => x.Name.First).MaximumLength(20);
                        return validator;
                    });
                    builder.Services.AddHeadlessOpenApi();
                    builder.Services.ConfigureHeadlessOpenApi(options =>
                    {
                        options.ThrowOnSchemaProcessingError = true;
                        options.FluentValidationRules.Insert(
                            0,
                            new FluentValidationRule
                            {
                                RuleName = "AlwaysFails",
                                Matches = validator => validator is FluentValidation.Validators.ILengthValidator,
                                Apply = _ => throw new InvalidOperationException("The nested rule failed."),
                            }
                        );
                    });
                },
                app =>
                    app.MapPost("/customers", ([FromBody] NestedCustomer customer) => TypedResults.Ok(customer))
                        .AllowAnonymous(),
                "v1",
                AbortToken
            );

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("The nested rule failed.");
    }

    [Fact]
    public async Task should_skip_an_instance_include_factory_and_keep_json_member_rules()
    {
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder.Services.AddScoped<IValidator<NestedCustomer>>(_ =>
                {
                    var validator = new InlineValidator<NestedCustomer>();
                    validator.RuleFor(x => x.Name).NotNull();
                    validator.RuleFor(x => x.Name.First).NotEmpty().MaximumLength(20);
                    validator.Include(x => new InstanceCustomerRules(x.Name.First!.Length));
                    validator.Include(new CustomerFamilyRules());
                    return validator;
                });
                builder.Services.AddHeadlessOpenApi();
            },
            app =>
                app.MapPost("/customers", ([FromBody] NestedCustomer customer) => TypedResults.Ok(customer))
                    .AllowAnonymous(),
            "v1",
            AbortToken
        );

        document.ShouldBeValidOpenApi("JSON rules with an instance-dependent Include factory");
        var schema = document.Schema("NestedCustomer");
        schema.Required().Should().Contain("name");
        var overlay = schema["properties"]!["name"]!["allOf"]![1]!;
        overlay.Required().Should().Contain("first");
        overlay["properties"]!["first"]!.ShouldBeJson("""{"type":"string","minLength":1,"maxLength":20}""");
        ((int)overlay["properties"]!["family"]!["maxLength"]!).Should().Be(30);
    }

    [Fact]
    public async Task should_apply_each_validator_once_root_first_when_json_includes_form_a_cycle()
    {
        var applied = new List<string>();
        var document = await OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                builder.Services.AddScoped<IValidator<PersonName>>(_ =>
                {
                    var first = new InlineValidator<PersonName>();
                    var second = new InlineValidator<PersonName>();
                    first.RuleFor(x => x.First).NotEmpty().MaximumLength(20);
                    second.RuleFor(x => x.Last).MaximumLength(30);
                    first.Include(second);
                    second.Include(first);
                    return first;
                });
                builder.Services.AddHeadlessOpenApi();
                builder.Services.ConfigureHeadlessOpenApi(options =>
                {
                    options.ThrowOnSchemaProcessingError = true;
                    options.FluentValidationRules.Add(
                        new FluentValidationRule
                        {
                            RuleName = "TrackIncludedRules",
                            Matches = validator => validator is FluentValidation.Validators.ILengthValidator,
                            Apply = context =>
                            {
                                // Fail on a repeated visit so a regression cannot overflow the test host's stack.
                                if (applied.Contains(context.PropertyKey, StringComparer.Ordinal))
                                {
                                    throw new InvalidOperationException(
                                        "An included validator was applied more than once."
                                    );
                                }

                                applied.Add(context.PropertyKey);
                            },
                        }
                    );
                });
            },
            app => app.MapPost("/names", ([FromBody] PersonName name) => TypedResults.NoContent()).AllowAnonymous(),
            "v1",
            AbortToken
        );

        document.ShouldBeValidOpenApi("JSON rules with a cyclic Include chain");
        var schema = document.Schema("PersonName");
        schema.Required().Should().Contain("first");
        schema["properties"]!["first"]!.ShouldBeJson("""{"type":"string","minLength":1,"maxLength":20}""");
        ((int)schema["properties"]!["family"]!["maxLength"]!).Should().Be(30);
        applied.Should().Equal("first", "family");
    }

    private static Task<JsonNode> _GetDocumentAsync(Action<OpenApiOptions>? configure = null)
    {
        return OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                _ConfigureServices(builder);
                if (configure is not null)
                {
                    builder.Services.AddOpenApi("v1", configure);
                }
            },
            app =>
            {
                app.MapPost("/customers", ([FromBody] NestedCustomer customer) => TypedResults.Ok(customer))
                    .AllowAnonymous();
                app.MapPost("/plain", ([FromBody] PlainCustomer customer) => TypedResults.Ok(customer))
                    .AllowAnonymous();
                app.MapPost("/nullable", ([FromBody] NullableCustomer customer) => TypedResults.Ok(customer))
                    .AllowAnonymous();
                app.MapPost("/deep", ([FromBody] DeepCustomer customer) => TypedResults.Ok(customer)).AllowAnonymous();
            },
            "v1",
            AbortToken
        );
    }

    private static void _ConfigureServices(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IValidator<NestedCustomer>, NestedCustomerValidator>();
        builder.Services.AddScoped<IValidator<NullableCustomer>, NullableCustomerValidator>();
        builder.Services.AddScoped<IValidator<DeepCustomer>, DeepCustomerValidator>();
        builder.Services.AddScoped<IValidator<OptionalLeafCustomer>, OptionalLeafCustomerValidator>();
        builder.Services.AddHeadlessOpenApi();
    }
}

public sealed class PersonName
{
    public string? First { get; set; }

    [JsonPropertyName("family")]
    public string? Last { get; set; }
}

public sealed class NestedCustomer
{
    public PersonName Name { get; set; } = new();
}

public sealed class PlainCustomer
{
    public PersonName Name { get; set; } = new();
}

public sealed class NullableCustomer
{
    public PersonName? Name { get; set; }
}

public sealed class CustomerAddress
{
    public PersonName Name { get; set; } = new();
}

public sealed class DeepCustomer
{
    public CustomerAddress Address { get; set; } = new();
}

public sealed class NestedCustomerValidator : AbstractValidator<NestedCustomer>
{
    public NestedCustomerValidator()
    {
        RuleFor(x => x.Name.First).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Name.First).MaximumLength(1).When(_ => false);
        Include(new CustomerFamilyRules());
    }
}

public sealed class CustomerFamilyRules : AbstractValidator<NestedCustomer>
{
    public CustomerFamilyRules()
    {
        RuleFor(x => x.Name.Last).MaximumLength(30);
    }
}

public sealed class InstanceCustomerRules : AbstractValidator<NestedCustomer>
{
    public InstanceCustomerRules(int maxLength)
    {
        RuleFor(x => x.Name.First).MaximumLength(maxLength);
    }
}

public sealed class NullableCustomerValidator : AbstractValidator<NullableCustomer>
{
    public NullableCustomerValidator()
    {
        RuleFor(x => x.Name!.First).NotEmpty().MaximumLength(20);
    }
}

public sealed class DeepCustomerValidator : AbstractValidator<DeepCustomer>
{
    public DeepCustomerValidator()
    {
        RuleFor(x => x.Address.Name.First).NotNull().MaximumLength(10);
        RuleFor(x => x.Address.Name.Last).MaximumLength(40);
    }
}

public sealed class OptionalNameAddress
{
    public PersonName? Name { get; set; }

    [JsonIgnore]
    public string? Secret { get; set; }
}

public sealed class OptionalLeafCustomer
{
    public OptionalNameAddress Address { get; set; } = new();
}

public sealed class OptionalLeafCustomerValidator : AbstractValidator<OptionalLeafCustomer>
{
    public OptionalLeafCustomerValidator()
    {
        RuleFor(x => x.Address.Name).NotNull();
        RuleFor(x => x.Address.Secret).MaximumLength(5);
    }
}
