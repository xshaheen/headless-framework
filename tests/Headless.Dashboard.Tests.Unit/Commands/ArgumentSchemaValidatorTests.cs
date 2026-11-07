// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Testing.Tests;

namespace Tests.Commands;

public sealed class ArgumentSchemaValidatorTests : TestBase
{
    private static readonly DashboardCommandArgument[] _Schema =
    [
        new()
        {
            Name = "count",
            Label = "Count",
            Kind = DashboardArgumentKind.WholeNumber,
            Required = true,
            Minimum = 1,
            Maximum = 10,
        },
        new()
        {
            Name = "ratio",
            Label = "Ratio",
            Kind = DashboardArgumentKind.Number,
            AllowedValues = ["0.5", "1"],
        },
        new()
        {
            Name = "note",
            Label = "Note",
            Kind = DashboardArgumentKind.Text,
            MaxLength = 3,
        },
        new()
        {
            Name = "force",
            Label = "Force",
            Kind = DashboardArgumentKind.Toggle,
        },
    ];

    [Fact]
    public void should_return_the_values_when_every_argument_matches_its_schema()
    {
        var result = _Validate("""{"count":3,"ratio":0.50,"note":"a😀b","force":true}""");

        result.IsValid.Should().BeTrue();
        result.Values["count"].GetInt32().Should().Be(3);
        result.Values["force"].GetBoolean().Should().BeTrue();
        result.Values.Should().ContainKeys("ratio", "note");
    }

    [Fact]
    public void should_accept_a_missing_optional_argument_and_a_null_one()
    {
        var result = _Validate("""{"count":1,"note":null}""");

        result.IsValid.Should().BeTrue();
        result.Values.Should().ContainSingle().Which.Key.Should().Be("count");
    }

    [Theory]
    [InlineData("""{"count":1.5}""", "arguments.count", "type")]
    [InlineData("""{"count":"1"}""", "arguments.count", "type")]
    [InlineData("""{"count":1,"force":"yes"}""", "arguments.force", "type")]
    [InlineData("""{"ratio":1}""", "arguments.count", "required")]
    [InlineData("""{"count":null}""", "arguments.count", "required")]
    [InlineData("""{"count":0}""", "arguments.count", "minimum")]
    [InlineData("""{"count":11}""", "arguments.count", "maximum")]
    [InlineData("""{"count":1,"ratio":0.25}""", "arguments.ratio", "enum")]
    [InlineData("""{"count":1,"note":"abcd"}""", "arguments.note", "maxLength")]
    [InlineData("""{"count":1,"extra":true}""", "arguments.extra", "additionalProperties")]
    [InlineData("[1]", "arguments", "type")]
    public void should_name_the_field_and_rule_when_an_argument_breaks_the_schema(
        string arguments,
        string field,
        string rule
    )
    {
        var result = _Validate(arguments);

        result.IsValid.Should().BeFalse();
        result.Values.Should().BeEmpty();
        var error = result.Errors.Should().ContainSingle().Which;
        error.Field.Should().Be(field);
        error.Rule.Should().Be(rule);
    }

    [Fact]
    public void should_report_the_required_argument_when_no_arguments_are_sent()
    {
        var result = ArgumentSchemaValidator.Validate(_Schema, arguments: null);

        result.Errors.Should().ContainSingle().Which.Rule.Should().Be("required");
    }

    [Fact]
    public void should_accept_no_arguments_when_the_command_declares_none()
    {
        ArgumentSchemaValidator.Validate([], arguments: null).IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_state_allowed_values_as_values_of_the_argument_kind_in_the_schema()
    {
        var schema = ArgumentSchema.Create(_Schema);

        schema.Required.Should().Equal("count");
        schema.Properties.Keys.Should().Equal("count", "ratio", "note", "force");
        schema.Properties["ratio"].Type.Should().Be("number");
        schema.Properties["ratio"].Enum!.Select(static e => e.GetDecimal()).Should().Equal(0.5m, 1m);
        schema.Properties["note"].MaxLength.Should().Be(3);
        schema.Properties["note"].Minimum.Should().BeNull();
        schema.Properties["force"].Type.Should().Be("boolean");
    }

    private static ArgumentValidationResult _Validate(string json)
    {
        using var document = JsonDocument.Parse(json);

        return ArgumentSchemaValidator.Validate(_Schema, document.RootElement);
    }
}
