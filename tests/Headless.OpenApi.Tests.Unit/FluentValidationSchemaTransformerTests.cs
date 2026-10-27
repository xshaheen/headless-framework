// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using FluentValidation;
using FluentValidation.Validators;
using Headless.OpenApi;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

public sealed class FluentValidationSchemaTransformerTests : TestBase
{
    private static Task<JsonNode> _GetDocumentAsync(
        CapturingLoggerProvider? logs = null,
        Action<HeadlessOpenApiOptions>? configure = null
    ) =>
        OpenApiTestHost.GetDocumentAsync(
            builder =>
            {
                if (logs is not null)
                {
                    builder.Logging.AddProvider(logs);
                    builder.Logging.SetMinimumLevel(LogLevel.Debug);
                }

                ValidatedApi.AddServices(builder);
                builder.Services.AddHeadlessOpenApi();

                if (configure is not null)
                {
                    builder.Services.ConfigureHeadlessOpenApi(configure);
                }
            },
            ValidatedApi.Map,
            "v1",
            AbortToken
        );

    [Fact]
    public async Task should_find_the_rules_of_a_property_renamed_in_json()
    {
        var document = await _GetDocumentAsync();

        ((int?)document.Schema("ContactRequest")["properties"]!["given_name"]!["maxLength"]).Should().Be(40);
    }

    [Fact]
    public async Task should_constrain_the_items_of_a_collection_rule_and_not_the_collection()
    {
        var document = await _GetDocumentAsync();
        var contact = document.Schema("ContactRequest");
        var aliases = contact["properties"]!["aliases"]!;

        aliases["items"]!.Types().Should().Equal("string");
        aliases.Types().Should().BeEquivalentTo(["null", "array"]);
        // RuleForEach skips a null collection, so the collection stays optional.
        contact.Required().Should().NotContain("aliases");
    }

    [Fact]
    public async Task should_remove_null_from_a_bound_parameter_that_must_not_be_null()
    {
        var document = await _GetDocumentAsync();
        var operation = document.Operation("/contacts", "get");

        operation.Parameter("page")["schema"]!.Types().Should().Equal("integer");
        operation.Parameter("term")["schema"]!.Types().Should().Equal("string");
    }

    [Fact]
    public async Task should_describe_a_validated_type_without_properties()
    {
        var document = await _GetDocumentAsync();

        document.Schema("EmptyRequest").Types().Should().Equal("object");
    }

    [Fact]
    public async Task should_log_and_skip_a_validator_that_cannot_be_resolved()
    {
        using var logs = new CapturingLoggerProvider();

        var document = await _GetDocumentAsync(logs);

        document.Schema("FaultyRequest")["properties"]!["name"]!["maxLength"].Should().BeNull();
        logs.Entries.Should()
            .Contain(entry =>
                entry.Level == LogLevel.Warning
                && entry.EventId.Id == 5
                && entry.Message.Contains("FaultyRequest", StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task should_log_each_type_and_rule_it_applies_at_debug()
    {
        using var logs = new CapturingLoggerProvider();

        await _GetDocumentAsync(logs);

        logs.Entries.Should()
            .Contain(entry =>
                entry.Level == LogLevel.Debug
                && entry.EventId.Id == 4
                && entry.Message.Contains("'ContactRequest'", StringComparison.Ordinal)
            );
        logs.Entries.Should()
            .Contain(entry =>
                entry.Level == LogLevel.Debug
                && entry.EventId.Id == 2
                && entry.Message.Contains("'Length'", StringComparison.Ordinal)
                && entry.Message.Contains("'ContactRequest.given_name'", StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task should_log_a_rule_that_fails_and_skip_it()
    {
        using var logs = new CapturingLoggerProvider();

        var document = await _GetDocumentAsync(
            logs,
            options =>
                options.FluentValidationRules.Add(
                    new FluentValidationRule
                    {
                        RuleName = "Broken",
                        Matches = validator => validator is ILengthValidator,
                        Apply = _ => throw new InvalidOperationException("broken rule"),
                    }
                )
        );

        ((int?)document.Schema("ContactRequest")["properties"]!["given_name"]!["maxLength"]).Should().Be(40);
        logs.Entries.Should()
            .Contain(entry =>
                entry.Level == LogLevel.Error
                && entry.EventId.Id == 3
                && entry.Message.Contains("'Broken'", StringComparison.Ordinal)
                && entry.Message.Contains("'ContactRequest.given_name'", StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task should_log_and_skip_an_instance_include_factory_and_keep_the_rules_already_applied()
    {
        using var logs = new CapturingLoggerProvider();

        var document = await _GetDocumentAsync(logs);

        document.ShouldBeValidOpenApi("JSON validation with an instance-dependent Include factory");
        ((int?)document.Schema("IncludeFailureRequest")["properties"]!["name"]!["maxLength"]).Should().Be(5);
        logs.Entries.Should()
            .Contain(entry =>
                entry.Level == LogLevel.Warning
                && entry.EventId.Id == 6
                && entry.Message.Contains("'IncludeFailureRequestValidator'", StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task should_fail_generation_on_an_include_that_fails_when_configured_to_throw()
    {
        var act = () => _GetDocumentAsync(configure: options => options.ThrowOnSchemaProcessingError = true);

        await act.Should().ThrowAsync<TargetInvocationException>();
    }
}

/// <summary>Endpoints whose validators reach the FluentValidation transformer's less common paths.</summary>
internal static class ValidatedApi
{
    public static void AddServices(WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IValidator<ContactRequest>, ContactRequestValidator>();
        builder.Services.AddScoped<IValidator<ContactQuery>, ContactQueryValidator>();
        builder.Services.AddScoped<IValidator<EmptyRequest>, EmptyRequestValidator>();
        builder.Services.AddScoped<IValidator<IncludeFailureRequest>, IncludeFailureRequestValidator>();
        builder.Services.AddScoped<IValidator<FaultyRequest>>(_ =>
            throw new InvalidOperationException("The validator cannot be built.")
        );
    }

    public static void Map(WebApplication app)
    {
        app.MapPost("/contacts", (ContactRequest request) => TypedResults.Ok(request.FirstName));
        app.MapGet("/contacts", ([AsParameters] ContactQuery query) => TypedResults.Ok(query.Page));
        app.MapPost("/empty", (EmptyRequest request) => TypedResults.Ok(request.GetHashCode()));
        app.MapPost("/faulty", (FaultyRequest request) => TypedResults.Ok(request.Name));
        app.MapPost("/include-failure", (IncludeFailureRequest request) => TypedResults.Ok(request.Name));
    }
}

public sealed class ContactRequest
{
    [JsonPropertyName("given_name")]
    public string? FirstName { get; init; }

    public List<string?>? Aliases { get; init; }
}

public sealed class ContactRequestValidator : AbstractValidator<ContactRequest>
{
    public ContactRequestValidator()
    {
        RuleFor(x => x.FirstName).MaximumLength(40);
        RuleForEach(x => x.Aliases).NotNull();
    }
}

public sealed class ContactQuery
{
    public int? Page { get; init; }

    public string? Term { get; init; }
}

public sealed class ContactQueryValidator : AbstractValidator<ContactQuery>
{
    public ContactQueryValidator()
    {
        RuleFor(x => x.Page).NotNull();
        RuleFor(x => x.Term).NotNull();
    }
}

public sealed class EmptyRequest;

public sealed class EmptyRequestValidator : AbstractValidator<EmptyRequest>
{
    public EmptyRequestValidator()
    {
        RuleFor(x => x).NotNull();
    }
}

public sealed class FaultyRequest
{
    public string? Name { get; init; }
}

public sealed class IncludeFailureRequest
{
    public string? Name { get; init; }
}

public sealed class IncludeFailureRequestValidator : AbstractValidator<IncludeFailureRequest>
{
    public IncludeFailureRequestValidator()
    {
        RuleFor(x => x.Name).MaximumLength(5);
        // The included validator depends on the instance, which the transformer does not have.
        Include(request => new IncludeFailurePartValidator(request.Name!.Length));
    }
}

public sealed class IncludeFailurePartValidator : AbstractValidator<IncludeFailureRequest>
{
    public IncludeFailurePartValidator(int maximumLength)
    {
        RuleFor(x => x.Name).MaximumLength(maximumLength);
    }
}
