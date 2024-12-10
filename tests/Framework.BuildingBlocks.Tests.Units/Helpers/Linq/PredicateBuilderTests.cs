// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Framework.BuildingBlocks.Helpers.Linq;

namespace Tests.Helpers.Linq;

public class PredicateBuilderTests
{
    [Fact]
    public void true_predicate_should_always_return_true_or_false_based_on_case_predicate()
    {
        // given
        var predicateAlwaysTrue = PredicateBuilder.True<int>();
        var predicateAlwaysFalse = PredicateBuilder.False<int>();

        // when
        var tureResult = predicateAlwaysTrue.Compile()(5);
        var falseResult = predicateAlwaysFalse.Compile()(5);

        // then
        tureResult.Should().BeTrue();
        falseResult.Should().BeFalse();
    }

    [Fact]
    public void not_predicate_should_invert_condition()
    {
        // given
        Expression<Func<int, bool>> isEven = x => x % 2 == 0;
        var notPredicate = isEven.Not();

        // when
        var result = notPredicate.Compile()(3);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void or_predicate_should_return_true_if_any_condition_is_true()
    {
        // given
        Expression<Func<int, bool>> isEven = x => x % 2 == 0;
        Expression<Func<int, bool>> isGreaterThanFive = x => x > 5;
        var orPredicate = isEven.Or(isGreaterThanFive);

        // when
        var result1 = orPredicate.Compile()(4);
        var result2 = orPredicate.Compile()(7);
        var result3 = orPredicate.Compile()(3);

        // then
        result1.Should().BeTrue("4 is even");
        result2.Should().BeTrue("7 is greater than 5");
        result3.Should().BeFalse("3 satisfies neither condition will be true when 4 or greater than 5");
    }

    [Fact]
    public void and_predicate_should_return_true_if_both_conditions_are_true()
    {
        // given
        Expression<Func<int, bool>> isEven = x => x % 2 == 0;
        Expression<Func<int, bool>> isGreaterThanFive = x => x > 5;
        var andPredicate = isEven.And(isGreaterThanFive);

        // when
        var result1 = andPredicate.Compile()(6);
        var result2 = andPredicate.Compile()(4);
        var result3 = andPredicate.Compile()(7);

        // then
        result1.Should().BeTrue("6 is even and greater than 5");
        result2.Should().BeFalse("4 is even but not greater than 5");
        result3.Should().BeFalse("7 is greater than 5 but not even");
    }

    [Fact]
    public void and_not_predicate_should_return_true_if_first_condition_is_true_and_second_is_false()
    {
        // given
        Expression<Func<int, bool>> isEven = x => x % 2 == 0;
        Expression<Func<int, bool>> isGreaterThanFive = x => x > 5;
        var andNotPredicate = isEven.AndNot(isGreaterThanFive);

        // when
        var result1 = andNotPredicate.Compile()(4);
        var result2 = andNotPredicate.Compile()(6);
        var result3 = andNotPredicate.Compile()(7);

        // then
        result1.Should().BeTrue("4 is even and not greater than 5");
        result2.Should().BeFalse("6 is even but greater than 5");
        result3.Should().BeFalse("7 does not satisfy the first condition");
    }

    [Fact]
    public void or_not_predicate_should_return_true_if_first_condition_is_true_or_second_is_false()
    {
        // given
        Expression<Func<int, bool>> isEven = x => x % 2 == 0;
        Expression<Func<int, bool>> isGreaterThanFive = x => x > 5;
        var orNotPredicate = isEven.OrNot(isGreaterThanFive);

        // when
        var result1 = orNotPredicate.Compile()(4);
        var result2 = orNotPredicate.Compile()(6);
        var result3 = orNotPredicate.Compile()(3);

        // then
        result1.Should().BeTrue("4 is even");
        result2.Should().BeTrue("6 is even");
        result3.Should().BeTrue("3 is not greater than 5");
    }

    [Fact]
    public void or_with_multiple_expressions_should_combine_them_correctly()
    {
        // given
        var predicates = new List<Expression<Func<int, bool>>> { x => x % 2 == 0, x => x > 5, x => x < 0 };

        var combinedOr = predicates.Or();

        // when
        var result1 = combinedOr.Compile()(4);
        var result2 = combinedOr.Compile()(7);
        var result3 = combinedOr.Compile()(-1);
        var result4 = combinedOr.Compile()(3);

        // then
        result1.Should().BeTrue("4 is even");
        result2.Should().BeTrue("7 is greater than 5");
        result3.Should().BeTrue("-1 is less than 0");
        result4.Should().BeFalse("3 satisfies none of the conditions");
    }

    [Fact]
    public void and_with_multiple_expressions_should_combine_them_correctly()
    {
        // given
        var predicates = new List<Expression<Func<int, bool>>> { x => x % 2 == 0, x => x > 5, x => x < 10 };

        var combinedAnd = predicates.And();

        // when
        var result1 = combinedAnd.Compile()(6);
        var result2 = combinedAnd.Compile()(4);
        var result3 = combinedAnd.Compile()(12);

        // then
        result1.Should().BeTrue("6 satisfies all conditions");
        result2.Should().BeFalse("4 does not satisfy all conditions");
        result3.Should().BeFalse("12 does not satisfy all conditions");
    }
}
