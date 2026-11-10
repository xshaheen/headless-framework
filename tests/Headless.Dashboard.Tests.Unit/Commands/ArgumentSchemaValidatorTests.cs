// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Dashboard;
using Headless.Testing.Tests;

namespace Tests.Commands;

public sealed class ArgumentSchemaValidatorTests : TestBase
{
    private static readonly DateTimeOffset _Now = new(2026, 12, 15, 8, 0, 0, TimeSpan.Zero);

    private static readonly DashboardCommandArgument[] _TimeAndBody =
    [
        new()
        {
            Name = "runAt",
            Label = "Run at",
            Kind = DashboardArgumentKind.DateTime,
            NotBeforeNow = true,
        },
        new()
        {
            Name = "request",
            Label = "Request",
            Kind = DashboardArgumentKind.Json,
            MaxLength = 20,
        },
    ];

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
        var result = ArgumentSchemaValidator.Validate(_Schema, arguments: null, _Now);

        result.Errors.Should().ContainSingle().Which.Rule.Should().Be("required");
    }

    [Fact]
    public void should_accept_no_arguments_when_the_command_declares_none()
    {
        ArgumentSchemaValidator.Validate([], arguments: null, _Now).IsValid.Should().BeTrue();
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

    [Theory]
    [InlineData("2026-12-15T08:00:00Z")]
    [InlineData("2026-12-15T10:00:00+02:00")]
    [InlineData("2026-12-15T10:00:00.0000000+02:00")]
    [InlineData("2026-12-15T03:00-05:00")]
    public void should_accept_an_instant_with_an_offset_that_is_not_in_the_past(string runAt)
    {
        var result = _ValidateTimeAndBody($$"""{"runAt":"{{runAt}}"}""");

        result.IsValid.Should().BeTrue();
        result.Values["runAt"].GetDateTimeOffset().Should().Be(_Now);
    }

    [Theory]
    [InlineData("2026-12-15T08:00:00")]
    [InlineData("2026-12-15")]
    [InlineData("2026-12-15T10:00:00+0200")]
    [InlineData("15/12/2026 08:00 +00:00")]
    [InlineData("tomorrow")]
    public void should_refuse_a_date_time_without_an_offset_naming_the_argument(string runAt)
    {
        var result = _ValidateTimeAndBody($$"""{"runAt":"{{runAt}}"}""");

        var error = result.Errors.Should().ContainSingle().Which;
        error.Field.Should().Be("arguments.runAt");
        error.Rule.Should().Be("format");
        error.Message.Should().Contain("'runAt'");
    }

    [Fact]
    public void should_refuse_an_instant_before_now_when_the_argument_asks_for_one_not_in_the_past()
    {
        var result = _ValidateTimeAndBody("""{"runAt":"2026-12-15T07:59:59Z"}""");

        var error = result.Errors.Should().ContainSingle().Which;
        error.Field.Should().Be("arguments.runAt");
        error.Rule.Should().Be("minimum");
    }

    [Fact]
    public void should_accept_valid_json_text_within_its_max_length()
    {
        var result = _ValidateTimeAndBody("""{"request":"{\"id\":1}"}""");

        result.IsValid.Should().BeTrue();
        result.Values["request"].GetString().Should().Be("""{"id":1}""");
    }

    [Theory]
    [InlineData("""{"request":"{\"id\":"}""", "format")]
    [InlineData("""{"request":"not json"}""", "format")]
    [InlineData("""{"request":"[1,2,3,4,5,6,7,8,9,10,11]"}""", "maxLength")]
    [InlineData("""{"request":{"id":1}}""", "type")]
    public void should_refuse_json_text_that_does_not_parse_or_is_too_long(string arguments, string rule)
    {
        var result = _ValidateTimeAndBody(arguments);

        var error = result.Errors.Should().ContainSingle().Which;
        error.Field.Should().Be("arguments.request");
        error.Rule.Should().Be(rule);
    }

    [Fact]
    public void should_describe_date_time_and_json_arguments_as_formatted_strings_in_the_schema()
    {
        var schema = ArgumentSchema.Create(_TimeAndBody);

        schema.Properties["runAt"].Type.Should().Be("string");
        schema.Properties["runAt"].Format.Should().Be("date-time");
        schema.Properties["runAt"].NotBeforeNow.Should().BeTrue();
        schema.Properties["request"].Type.Should().Be("string");
        schema.Properties["request"].ContentMediaType.Should().Be("application/json");
        schema.Properties["request"].MaxLength.Should().Be(20);
        schema.Properties["request"].NotBeforeNow.Should().BeNull();
    }

    private static ArgumentValidationResult _ValidateTimeAndBody(string json)
    {
        using var document = JsonDocument.Parse(json);

        return ArgumentSchemaValidator.Validate(_TimeAndBody, document.RootElement, _Now);
    }

    private static ArgumentValidationResult _Validate(string json)
    {
        using var document = JsonDocument.Parse(json);

        return ArgumentSchemaValidator.Validate(_Schema, document.RootElement, _Now);
    }
}
