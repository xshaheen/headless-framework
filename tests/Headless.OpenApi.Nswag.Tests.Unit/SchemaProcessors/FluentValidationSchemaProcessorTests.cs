// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using FluentValidation;
using FluentValidation.Validators;
using Headless.OpenApi.Nswag;
using Headless.OpenApi.Nswag.SchemaProcessors;
using Headless.OpenApi.Nswag.SchemaProcessors.FluentValidation.Models;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Namotion.Reflection;
using NJsonSchema;
using NJsonSchema.Generation;

namespace Tests.SchemaProcessors;

public sealed class FluentValidationSchemaProcessorTests : TestBase
{
    [Fact]
    public void should_project_common_validator_constraints_to_an_object_schema()
    {
        using var services = _CreateServices(new RequestValidator());
        var schema = _CreateObjectSchema(
            ("Name", JsonObjectType.String | JsonObjectType.Null),
            ("Age", JsonObjectType.Integer),
            ("Score", JsonObjectType.Number),
            ("Email", JsonObjectType.String)
        );

        new FluentValidationSchemaProcessor(services).Process(
            _CreateContext(typeof(Request).ToContextualType(), schema)
        );

        schema.RequiredProperties.Should().Contain("Name");
        schema.Properties["Name"].IsNullableRaw.Should().BeFalse();
        schema.Properties["Name"].Type.Should().NotHaveFlag(JsonObjectType.Null);
        schema.Properties["Name"].MinLength.Should().Be(1);
        schema.Properties["Name"].MaxLength.Should().Be(10);
        schema.Properties["Name"].Pattern.Should().Be("^[a-z]+$");
        schema.Properties["Age"].Minimum.Should().Be(18);
        schema.Properties["Age"].Maximum.Should().Be(100);
        schema.Properties["Age"].IsExclusiveMaximum.Should().BeTrue();
        schema.Properties["Score"].ExclusiveMinimum.Should().Be(1);
        schema.Properties["Score"].ExclusiveMaximum.Should().Be(10);
        schema.Properties["Email"].Pattern.Should().Be("^[^@]+@[^@]+$");
    }

    [Fact]
    public void should_apply_declaring_type_rules_when_a_property_schema_is_processed_directly()
    {
        using var services = _CreateServices(new RequestValidator());
        var propertyType = typeof(Request)
            .GetProperty(nameof(Request.Name), BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
            .ToContextualProperty()
            .PropertyType;
        var schema = new JsonSchema { Type = JsonObjectType.String | JsonObjectType.Null };

        new FluentValidationSchemaProcessor(services).Process(_CreateContext(propertyType, schema));

        schema.IsNullableRaw.Should().BeFalse();
        schema.MinLength.Should().Be(1);
        schema.MaxLength.Should().Be(10);
        schema.Pattern.Should().Be("^[a-z]+$");
    }

    [Fact]
    public void should_replace_a_default_rule_when_a_custom_rule_uses_the_same_name()
    {
        using var services = _CreateServices(new RequestValidator());
        var schema = _CreateObjectSchema(("Name", JsonObjectType.String));
        FluentValidationRule replacement = new()
        {
            RuleName = FluentValidationRule.LengthRule.RuleName,
            Matches = validator => validator is ILengthValidator,
            Apply = context => context.PropertySchema.MaxLength = 77,
        };

        new FluentValidationSchemaProcessor(services, rules: [replacement]).Process(
            _CreateContext(typeof(Request).ToContextualType(), schema)
        );

        schema.Properties["Name"].MaxLength.Should().Be(77);
        schema.Properties["Name"].MinLength.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void should_honor_the_error_policy_when_a_custom_rule_throws(bool throwOnError)
    {
        using var services = _CreateServices(new RequestValidator());
        var schema = _CreateObjectSchema(("Name", JsonObjectType.String));
        FluentValidationRule failingRule = new()
        {
            RuleName = "FailingRule",
            Matches = validator => validator is INotEmptyValidator,
            Apply = _ => throw new InvalidOperationException("rule failed"),
        };
        var processor = new FluentValidationSchemaProcessor(
            services,
            new HeadlessNswagOptions { ThrowOnSchemaProcessingError = throwOnError },
            [failingRule]
        );

        var act = () => processor.Process(_CreateContext(typeof(Request).ToContextualType(), schema));

        if (throwOnError)
        {
            act.Should().Throw<InvalidOperationException>().WithMessage("rule failed");
        }
        else
        {
            act.Should().NotThrow();
        }
    }

    [Fact]
    public void should_leave_the_schema_unchanged_when_no_validator_is_registered()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var schema = _CreateObjectSchema(("Name", JsonObjectType.String));

        new FluentValidationSchemaProcessor(services).Process(
            _CreateContext(typeof(Request).ToContextualType(), schema)
        );

        schema.RequiredProperties.Should().BeEmpty();
        schema.Properties["Name"].MaxLength.Should().BeNull();
    }

    private static ServiceProvider _CreateServices(IValidator<Request> validator)
    {
        return new ServiceCollection()
            .AddSingleton(validator)
            .AddSingleton<IValidator<Request>>(validator)
            .BuildServiceProvider();
    }

    private static JsonSchema _CreateObjectSchema(params (string Name, JsonObjectType Type)[] properties)
    {
        var schema = new JsonSchema { Type = JsonObjectType.Object };
        foreach (var (name, type) in properties)
        {
            schema.Properties[name] = new JsonSchemaProperty { Type = type };
        }

        return schema;
    }

    private static SchemaProcessorContext _CreateContext(ContextualType contextualType, JsonSchema schema)
    {
        var settings = new SystemTextJsonSchemaGeneratorSettings();
        return new SchemaProcessorContext(
            contextualType,
            schema,
            new JsonSchemaResolver(schema, settings),
            new JsonSchemaGenerator(settings),
            settings
        );
    }

    private sealed class Request
    {
        public string? Name { get; set; }

        public int Age { get; set; }

        public decimal Score { get; set; }

        public string? Email { get; set; }
    }

    private sealed class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(request => request.Name).NotEmpty().Length(2, 10).Matches("^[a-z]+$");
            RuleFor(request => request.Age).GreaterThanOrEqualTo(18).LessThan(100);
            RuleFor(request => request.Score).ExclusiveBetween(1, 10);
            RuleFor(request => request.Email).EmailAddress(EmailValidationMode.AspNetCoreCompatible);
        }
    }
}
