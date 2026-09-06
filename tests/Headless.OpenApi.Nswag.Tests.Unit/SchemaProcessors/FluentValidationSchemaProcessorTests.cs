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
            ("Code", JsonObjectType.String | JsonObjectType.Null),
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
        schema.Properties["Name"].MinLength.Should().Be(2);
        schema.Properties["Name"].MaxLength.Should().Be(10);
        schema.Properties["Name"].Pattern.Should().Be("^[a-z]+$");
        schema.RequiredProperties.Should().Contain("Code");
        schema.Properties["Code"].MinLength.Should().Be(1);
        schema.Properties["Age"].Minimum.Should().Be(18);
        schema.Properties["Age"].IsExclusiveMinimum.Should().BeFalse();
        schema.Properties["Age"].Maximum.Should().Be(100);
        schema.Properties["Age"].IsExclusiveMaximum.Should().BeTrue();
        schema.Properties["Score"].Minimum.Should().Be(1);
        schema.Properties["Score"].IsExclusiveMinimum.Should().BeTrue();
        schema.Properties["Score"].Maximum.Should().Be(10);
        schema.Properties["Score"].IsExclusiveMaximum.Should().BeTrue();
        schema.Properties["Email"].Pattern.Should().Be("^[^@]+@[^@]+$");
    }

    [Fact]
    public void should_write_exclusive_bounds_in_the_openapi_30_form_for_every_comparison()
    {
        // NSwag emits `openapi: 3.0.0`, where `exclusiveMinimum`/`exclusiveMaximum` are booleans that
        // modify `minimum`/`maximum`. NJsonSchema also exposes the JSON Schema draft-6 numeric form on
        // the same object; writing that leaves the document with no `minimum` at all, so 3.0 tooling
        // silently drops the bound and strict client generators reject the schema.
        using var services = _CreateServices(new BoundsValidator());
        var schema = _CreateObjectSchema(
            ("AboveZero", JsonObjectType.Number),
            ("AtLeastOne", JsonObjectType.Number),
            ("BelowTen", JsonObjectType.Number),
            ("AtMostTen", JsonObjectType.Number),
            ("Between", JsonObjectType.Number)
        );

        new FluentValidationSchemaProcessor(services).Process(
            _CreateContext(typeof(Bounds).ToContextualType(), schema)
        );

        schema.Properties["AboveZero"].Minimum.Should().Be(0);
        schema.Properties["AboveZero"].IsExclusiveMinimum.Should().BeTrue();
        schema.Properties["AtLeastOne"].Minimum.Should().Be(1);
        schema.Properties["AtLeastOne"].IsExclusiveMinimum.Should().BeFalse();
        schema.Properties["BelowTen"].Maximum.Should().Be(10);
        schema.Properties["BelowTen"].IsExclusiveMaximum.Should().BeTrue();
        schema.Properties["AtMostTen"].Maximum.Should().Be(10);
        schema.Properties["AtMostTen"].IsExclusiveMaximum.Should().BeFalse();
        schema.Properties["Between"].Minimum.Should().Be(2);
        schema.Properties["Between"].Maximum.Should().Be(8);

        foreach (var property in schema.Properties.Values)
        {
            property.ExclusiveMinimum.Should().BeNull();
            property.ExclusiveMaximum.Should().BeNull();
        }
    }

    [Fact]
    public void should_constrain_the_item_schema_when_the_rule_came_from_rule_for_each()
    {
        // RuleForEach validates each ELEMENT, but FluentValidation reports the rule under the collection
        // property's own name. Left on the array schema, `minimum` and `maxLength` mean nothing — JSON
        // Schema sizes arrays with minItems/maxItems — and generators emit nonsense such as
        // `z.array(...).gt(0)`.
        using var services = _CreateServices(new BasketValidator());
        var schema = new JsonSchema { Type = JsonObjectType.Object };
        schema.Properties["Quantities"] = new JsonSchemaProperty
        {
            Type = JsonObjectType.Array,
            Item = new JsonSchema { Type = JsonObjectType.Integer },
        };
        schema.Properties["Labels"] = new JsonSchemaProperty
        {
            Type = JsonObjectType.Array,
            Item = new JsonSchema { Type = JsonObjectType.String },
        };

        new FluentValidationSchemaProcessor(services).Process(
            _CreateContext(typeof(Basket).ToContextualType(), schema)
        );

        schema.Properties["Quantities"].Item!.Minimum.Should().Be(0);
        schema.Properties["Quantities"].Item!.IsExclusiveMinimum.Should().BeTrue();
        schema.Properties["Quantities"].Minimum.Should().BeNull();
        schema.Properties["Quantities"].Maximum.Should().BeNull();

        schema.Properties["Labels"].Item!.MaxLength.Should().Be(5);
        schema.Properties["Labels"].MaxLength.Should().BeNull();
    }

    [Fact]
    public void should_constrain_the_property_itself_when_the_rule_is_not_a_collection_rule()
    {
        // Guard for the redirect above: a plain RuleFor on a collection property still targets the array.
        using var services = _CreateServices(new BasketValidator());
        var schema = new JsonSchema { Type = JsonObjectType.Object };
        schema.Properties["Tags"] = new JsonSchemaProperty
        {
            Type = JsonObjectType.Array,
            Item = new JsonSchema { Type = JsonObjectType.String },
        };

        new FluentValidationSchemaProcessor(services).Process(
            _CreateContext(typeof(Basket).ToContextualType(), schema)
        );

        schema.Properties["Tags"].MinLength.Should().Be(1);
        schema.Properties["Tags"].Item!.MinLength.Should().BeNull();
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
        schema.MinLength.Should().Be(2);
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
        schema.Properties["Name"].MinLength.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void should_honor_the_error_policy_when_a_custom_rule_throws(bool throwOnError)
    {
        using var services = _CreateServices(new RequestValidator());
        var schema = _CreateObjectSchema(("Name", JsonObjectType.String), ("Code", JsonObjectType.String));
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

    private static ServiceProvider _CreateServices<T>(IValidator<T> validator)
    {
        return new ServiceCollection()
            .AddSingleton(validator)
            .AddSingleton<IValidator<T>>(validator)
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

        public string? Code { get; set; }

        public int Age { get; set; }

        public decimal Score { get; set; }

        public string? Email { get; set; }
    }

    private sealed class RequestValidator : AbstractValidator<Request>
    {
        public RequestValidator()
        {
            RuleFor(request => request.Name).NotNull().Length(2, 10).Matches("^[a-z]+$");
            RuleFor(request => request.Code).NotEmpty();
            RuleFor(request => request.Age).GreaterThanOrEqualTo(18).LessThan(100);
            RuleFor(request => request.Score).ExclusiveBetween(1, 10);
            RuleFor(request => request.Email).EmailAddress(EmailValidationMode.AspNetCoreCompatible);
        }
    }

    private sealed class Bounds
    {
        public decimal AboveZero { get; set; }

        public decimal AtLeastOne { get; set; }

        public decimal BelowTen { get; set; }

        public decimal AtMostTen { get; set; }

        public decimal Between { get; set; }
    }

    private sealed class BoundsValidator : AbstractValidator<Bounds>
    {
        public BoundsValidator()
        {
            RuleFor(bounds => bounds.AboveZero).GreaterThan(0);
            RuleFor(bounds => bounds.AtLeastOne).GreaterThanOrEqualTo(1);
            RuleFor(bounds => bounds.BelowTen).LessThan(10);
            RuleFor(bounds => bounds.AtMostTen).LessThanOrEqualTo(10);
            RuleFor(bounds => bounds.Between).InclusiveBetween(2, 8);
        }
    }

    private sealed class Basket
    {
        public List<int> Quantities { get; set; } = [];

        public List<string> Labels { get; set; } = [];

        public List<string> Tags { get; set; } = [];
    }

    private sealed class BasketValidator : AbstractValidator<Basket>
    {
        public BasketValidator()
        {
            RuleForEach(basket => basket.Quantities).GreaterThan(0);
            RuleForEach(basket => basket.Labels).MaximumLength(5);
            RuleFor(basket => basket.Tags).NotEmpty();
        }
    }
}
