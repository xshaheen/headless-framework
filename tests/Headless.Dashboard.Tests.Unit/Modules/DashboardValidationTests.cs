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
