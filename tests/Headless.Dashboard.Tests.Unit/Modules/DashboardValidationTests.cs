// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Dashboard;
using Headless.Testing.Tests;

namespace Tests.Modules;

/// <summary>
/// A module endpoint refuses input in the same shape a command does: field names as the JSON body spells them, each with
/// its messages, and a <c>rules</c> extension naming the rule each field broke.
/// </summary>
public sealed class DashboardValidationTests : TestBase
{
    [Fact]
    public void should_name_each_field_as_the_body_spells_it_with_its_rule_and_messages()
    {
        var result = new StepValidator().Validate(
            new Step(Function: "", Retries: -1, Children: [new Step(Function: "", Retries: 0, Children: [])])
        );

        var problem = DashboardValidation.Problem(result).ProblemDetails;

        problem.Status.Should().Be(400);
        problem.Errors.Keys.Should().BeEquivalentTo("function", "retries", "children[0].function");
        problem.Errors["retries"].Should().Equal("Retries cannot be negative.");
        problem
            .Extensions["rules"]
            .Should()
            .BeEquivalentTo(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["function"] = DashboardValidation.RequiredRule,
                    ["retries"] = "minimum",
                    ["children[0].function"] = DashboardValidation.RequiredRule,
                }
            );
        // The first failure doubles as the detail, so a caller that shows one line shows the first problem.
        problem.Detail.Should().Be("Choose a function.");
    }

    [Fact]
    public void should_answer_one_refused_field_with_its_message_as_the_detail()
    {
        var problem = DashboardValidation
            .Problem("expression", "format", "The expression needs six fields.")
            .ProblemDetails;

        problem.Errors.Should().ContainKey("expression").WhoseValue.Should().Equal("The expression needs six fields.");
        problem
            .Extensions["rules"]
            .Should()
            .BeEquivalentTo(new Dictionary<string, string>(StringComparer.Ordinal) { ["expression"] = "format" });
        problem.Detail.Should().Be("The expression needs six fields.");
    }

    [Theory]
    [InlineData("Friday", DayOfWeek.Friday)]
    [InlineData("friday", DayOfWeek.Friday)]
    [InlineData("FRIDAY", DayOfWeek.Friday)]
    [InlineData("5", DayOfWeek.Friday)]
    [InlineData("7", null)]
    [InlineData("Fri", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void should_read_a_declared_member_ignoring_case_and_nothing_else(string? value, DayOfWeek? expected)
    {
        DashboardValidation.ParseEnum<DayOfWeek>(value).Should().Be(expected);
    }

    [Theory]
    [InlineData("Friday", true, DayOfWeek.Friday)]
    [InlineData("friday", true, DayOfWeek.Friday)]
    [InlineData("FRIDAY", true, DayOfWeek.Friday)]
    [InlineData("5", false, default(DayOfWeek))]
    [InlineData("7", false, default(DayOfWeek))]
    [InlineData("Monday,Tuesday", false, default(DayOfWeek))]
    [InlineData(" Friday", false, default(DayOfWeek))]
    [InlineData("Fri", false, default(DayOfWeek))]
    [InlineData("", false, default(DayOfWeek))]
    [InlineData(null, false, default(DayOfWeek))]
    public void should_parse_only_declared_enum_names_ignoring_case_and_refuse_numbers_and_combinations(
        string? value,
        bool expectedParsed,
        DayOfWeek expectedValue
    )
    {
        var parsed = DashboardValidation.TryParseEnumName<DayOfWeek>(value, out var result);

        parsed.Should().Be(expectedParsed);
        result.Should().Be(expectedValue);
    }

    private sealed record Step(string Function, int Retries, IReadOnlyList<Step> Children);

    private sealed class StepValidator : AbstractValidator<Step>
    {
        public StepValidator()
        {
            RuleFor(step => step.Function).NotEmpty().WithMessage("Choose a function.");
            RuleFor(step => step.Retries)
                .GreaterThanOrEqualTo(0)
                .WithErrorCode("minimum")
                .WithMessage("Retries cannot be negative.");
            RuleForEach(step => step.Children).SetValidator(this);
        }
    }
}
