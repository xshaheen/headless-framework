namespace Tests.Extensions.Collections;

public sealed class ListExtensionsTests
{
    [Fact]
    public void InsertRange_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();
        list.InsertRange(1, [7, 8, 9]);

        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 7);
        list.Should().HaveElementAt(2, 8);
        list.Should().HaveElementAt(3, 9);
        list.Should().HaveElementAt(4, 2);
        list.Should().HaveElementAt(5, 3);
    }

    [Fact]
    public void InsertAfter_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.InsertAfter(2, 42);

        list.Should().HaveCount(4);
        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 2);
        list.Should().HaveElementAt(2, 42);
        list.Should().HaveElementAt(3, 3);

        list.InsertAfter(3, 43);

        list.Should().HaveCount(5);
        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 2);
        list.Should().HaveElementAt(2, 42);
        list.Should().HaveElementAt(3, 3);
        list.Should().HaveElementAt(4, 43);
    }

    [Fact]
    public void InsertAfter_with_predicate_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.InsertAfter(i => i == 2, 42);

        list.Should().HaveCount(4);
        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 2);
        list.Should().HaveElementAt(2, 42);
        list.Should().HaveElementAt(3, 3);

        list.InsertAfter(i => i == 3, 43);

        list.Should().HaveCount(5);
        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 2);
        list.Should().HaveElementAt(2, 42);
        list.Should().HaveElementAt(3, 3);
        list.Should().HaveElementAt(4, 43);
    }

    [Fact]
    public void InsertAfter_with_predicate__should_insert_to_first_if_not_found()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.InsertAfter(i => i == 999, 42);

        list.Should().HaveCount(4);
        list.Should().HaveElementAt(0, 42);
        list.Should().HaveElementAt(1, 1);
        list.Should().HaveElementAt(2, 2);
        list.Should().HaveElementAt(3, 3);
    }

    [Fact]
    public void InsertBefore_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.InsertBefore(2, 42);

        list.Should().HaveCount(4);
        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 42);
        list.Should().HaveElementAt(2, 2);
        list.Should().HaveElementAt(3, 3);

        list.InsertBefore(1, 43);

        list.Should().HaveCount(5);
        list.Should().HaveElementAt(0, 43);
        list.Should().HaveElementAt(1, 1);
        list.Should().HaveElementAt(2, 42);
        list.Should().HaveElementAt(3, 2);
        list.Should().HaveElementAt(4, 3);
    }

    [Fact]
    public void InsertBefore_with_predicate_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.InsertBefore(i => i == 2, 42);

        list.Should().HaveCount(4);
        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 42);
        list.Should().HaveElementAt(2, 2);
        list.Should().HaveElementAt(3, 3);

        list.InsertBefore(i => i == 1, 43);

        list.Should().HaveCount(5);
        list.Should().HaveElementAt(0, 43);
        list.Should().HaveElementAt(1, 1);
        list.Should().HaveElementAt(2, 42);
        list.Should().HaveElementAt(3, 2);
        list.Should().HaveElementAt(4, 3);
    }

    [Fact]
    public void ReplaceWhile_with_value_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.ReplaceWhile(i => i >= 2, 42);

        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 42);
        list.Should().HaveElementAt(2, 42);
    }

    [Fact]
    public void ReplaceWhile_with_factory_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.ReplaceWhile(i => i >= 2, i => i + 1);

        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 3);
        list.Should().HaveElementAt(2, 4);
    }

    [Fact]
    public void ReplaceFirst_with_value_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.ReplaceFirst(i => i >= 2, 42);

        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 42);
        list.Should().HaveElementAt(2, 3);
    }

    [Fact]
    public void ReplaceFirst_with_factory_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.ReplaceFirst(i => i >= 2, i => i + 1);

        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 3);
        list.Should().HaveElementAt(2, 3);
    }

    [Fact]
    public void ReplaceFirst_with_item_tests()
    {
        var list = Enumerable.Range(1, 3).ToList();

        list.ReplaceFirst(2, 42);

        list.Should().HaveElementAt(0, 1);
        list.Should().HaveElementAt(1, 42);
        list.Should().HaveElementAt(2, 3);
    }
}
