// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class HasNoDuplicatesTests
{
    [Fact]
    public void has_no_duplicates_should_return_collection_when_unique()
    {
        var collection = new[] { 1, 2, 3 };
        Argument.HasNoDuplicates(collection).Should().BeSameAs(collection);
    }

    [Fact]
    public void has_no_duplicates_should_throw_when_duplicate_present()
    {
        var collection = new[] { 1, 2, 2, 3 };
        var action = () => Argument.HasNoDuplicates(collection);

        action
            .Should()
            .ThrowExactly<ArgumentException>()
            .WithMessage(
                "The argument \"collection\" must not contain duplicate items (Duplicate <2>). (Parameter 'collection')"
            );
    }

    [Fact]
    public void has_no_duplicates_should_throw_argument_null_when_null()
    {
        var action = () => Argument.HasNoDuplicates<int>(null);
        action.Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void has_no_duplicates_should_honor_comparer()
    {
        var collection = new[] { "a", "A", "b" };

        Argument.HasNoDuplicates(collection, StringComparer.Ordinal).Should().BeSameAs(collection);

        var action = () => Argument.HasNoDuplicates(collection, StringComparer.OrdinalIgnoreCase);
        action.Should().ThrowExactly<ArgumentException>();
    }
}
