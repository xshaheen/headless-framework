// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Testing.Tests;

namespace Tests.Validation;

public sealed class HeadlessStorageIndexKeyValidatorsTests : TestBase
{
    [Theory]
    [InlineData(800, 50, true)]
    [InlineData(800, 51, false)]
    [InlineData(4000, 0, false)]
    public void should_bound_the_combined_index_key_at_850_characters(int first, int second, bool valid)
    {
        // given
        var validator = new KeyValidator();

        // when
        var result = validator.Validate(new KeyOptions { First = first, Second = second });

        // then
        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public void should_name_the_index_its_width_and_each_column_length_when_refused()
    {
        // when
        var result = new KeyValidator().Validate(new KeyOptions { First = 800, Second = 51 });

        // then
        var error = result.Errors.Should().ContainSingle().Subject;
        error.PropertyName.Should().Be("First + Second");
        error
            .ErrorMessage.Should()
            .Be(
                "The index Keys (First, Second) would be up to 1702 bytes wide, past SQL Server's 1700-byte index key "
                    + "limit: First (800) + Second (51) must total at most 850 characters."
            );
    }

    [Fact]
    public void should_leave_a_negative_length_to_the_column_length_rule()
    {
        // when
        var result = new KeyValidator().Validate(new KeyOptions { First = -5000, Second = 850 });

        // then
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void should_refuse_a_column_selector_that_is_not_a_property()
    {
        // when
        var act = () => new ComputedKeyValidator();

        // then
        act.Should().Throw<ArgumentException>().WithParameterName("column");
    }

    private sealed class KeyOptions
    {
        public int First { get; init; }

        public int Second { get; init; }
    }

    private sealed class KeyValidator : AbstractValidator<KeyOptions>
    {
        public KeyValidator()
        {
            this.AddSqlServerIndexKeyRule("Keys (First, Second)", x => x.First, x => x.Second);
        }
    }

    private sealed class ComputedKeyValidator : AbstractValidator<KeyOptions>
    {
        public ComputedKeyValidator()
        {
            this.AddSqlServerIndexKeyRule("Keys (First)", x => x.First + 1);
        }
    }
}
