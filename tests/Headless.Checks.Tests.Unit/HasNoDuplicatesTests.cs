// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Tests;

public sealed class HasNoDuplicatesTests
{
    [Fact]
    public void should_return_collection_when_has_no_duplicates_unique()
    {
        var collection = new[] { 1, 2, 3 };
        Argument.HasNoDuplicates(collection).Should().BeSameAs(collection);
    }

    [Fact]
    public void should_throw_when_has_no_duplicates_duplicate_present()
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
    public void should_throw_argument_null_when_has_no_duplicates_null()
    {
        var action = () => Argument.HasNoDuplicates<int>(null);
        action.Should().ThrowExactly<ArgumentNullException>();
    }

    [Fact]
    public void should_honor_comparer_when_has_no_duplicates()
    {
        var collection = new[] { "a", "A", "b" };

        Argument.HasNoDuplicates(collection, StringComparer.Ordinal).Should().BeSameAs(collection);

        var action = () => Argument.HasNoDuplicates(collection, StringComparer.OrdinalIgnoreCase);
        action.Should().ThrowExactly<ArgumentException>();
    }
}
