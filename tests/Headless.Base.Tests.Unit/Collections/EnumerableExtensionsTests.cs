// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests.Collections;

public sealed class EnumerableExtensionsTests
{
    // EmptyIfNull tests

    [Fact]
    public void empty_if_null_should_return_empty_when_null()
    {
        // given
        IEnumerable<int>? source = null;

        // when
        var result = source.EmptyIfNull();

        // then
        result.Should().BeEmpty();
    }

    [Fact]
    public void empty_if_null_should_return_source_when_not_null()
    {
        // given
        IEnumerable<int> source = [1, 2, 3];

        // when
        var result = source.EmptyIfNull();

        // then
        result.Should().BeEquivalentTo([1, 2, 3]);
    }

    // AsICollection tests

    [Fact]
    public void as_icollection_should_return_same_instance_when_already_collection()
    {
        // given
        ICollection<int> source = [1, 2, 3];

        // when
        var result = source.AsICollection();

        // then
        result.Should().BeSameAs(source);
    }

    [Fact]
    public void as_icollection_should_materialize_enumerable()
    {
        // given
        IEnumerable<int> source = Enumerable.Range(1, 3);

        // when
        var result = source.AsICollection();

        // then
        result.Should().BeEquivalentTo([1, 2, 3]);
    }

    // AsList tests

    [Fact]
    public void as_list_should_return_same_instance_when_already_list()
    {
        // given
        var source = new List<int> { 1, 2, 3 };

        // when
        var result = source.AsList();

        // then
        result.Should().BeSameAs(source);
    }

    [Fact]
    public void as_list_should_materialize_enumerable()
    {
        // given
        IEnumerable<int> source = Enumerable.Range(1, 3);

        // when
        var result = source.AsList();

        // then
        result.Should().BeEquivalentTo([1, 2, 3]);
    }

    // AsIList tests

    [Fact]
    public void as_ilist_should_return_same_instance_when_already_list()
    {
        // given
        var source = new List<int> { 1, 2, 3 };

        // when
        var result = source.AsIList();

        // then
        result.Should().BeSameAs(source);
    }

    [Fact]
    public void as_ilist_should_materialize_enumerable()
    {
        // given
        IEnumerable<int> source = Enumerable.Range(1, 3);

        // when
        var result = source.AsIList();

        // then
        result.Should().BeEquivalentTo([1, 2, 3]);
    }

    // AsArray tests

    [Fact]
    public void as_array_should_return_same_instance_when_already_array()
    {
        // given
        int[] source = [1, 2, 3];

        // when
        var result = source.AsArray();

        // then
        result.Should().BeSameAs(source);
    }

    [Fact]
    public void as_array_should_materialize_enumerable()
    {
        // given
        IEnumerable<int> source = Enumerable.Range(1, 3);

        // when
        var result = source.AsArray();

        // then
        result.Should().BeEquivalentTo([1, 2, 3]);
    }

    // AsISet tests

    [Fact]
    public void as_iset_should_return_same_instance_when_already_set()
    {
        // given
        ISet<int> source = new HashSet<int> { 1, 2, 3 };

        // when
        var result = source.AsISet();

        // then
        result.Should().BeSameAs(source);
    }

    [Fact]
    public void as_iset_should_materialize_enumerable()
    {
        // given
        IEnumerable<int> source = [1, 2, 2, 3];

        // when
        var result = source.AsISet();

        // then
        result.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public void as_iset_should_use_provided_comparer()
    {
        // given
        IEnumerable<string> source = ["a", "A", "b"];

        // when
        var result = source.AsISet(StringComparer.OrdinalIgnoreCase);

        // then
        result.Should().HaveCount(2);
    }

    // AsHashSet tests

    [Fact]
    public void as_hashset_should_return_same_instance_when_already_hashset()
    {
        // given
        var source = new HashSet<int> { 1, 2, 3 };

        // when
        var result = source.AsHashSet();

        // then
        result.Should().BeSameAs(source);
    }

    [Fact]
    public void as_hashset_should_materialize_enumerable()
    {
        // given
        IEnumerable<int> source = [1, 2, 2, 3];

        // when
        var result = source.AsHashSet();

        // then
        result.Should().BeEquivalentTo([1, 2, 3]);
    }

    // AsIReadOnlyCollection tests

    [Fact]
    public void as_ireadonlycollection_should_return_same_instance_when_already_readonly()
    {
        // given
        IReadOnlyCollection<int> source = [1, 2, 3];

        // when
        var result = source.AsIReadOnlyCollection();

        // then
        result.Should().BeSameAs(source);
    }

    [Fact]
    public void as_ireadonlycollection_should_materialize_enumerable()
    {
        // given
        IEnumerable<int> source = Enumerable.Range(1, 3);

        // when
        var result = source.AsIReadOnlyCollection();

        // then
        result.Should().BeEquivalentTo([1, 2, 3]);
    }

    // AsIReadOnlyList tests

    [Fact]
    public void as_ireadonlylist_should_return_same_instance_when_already_readonly()
    {
        // given
        IReadOnlyList<int> source = [1, 2, 3];

        // when
        var result = source.AsIReadOnlyList();

        // then
        result.Should().BeSameAs(source);
    }

    [Fact]
    public void as_ireadonlylist_should_materialize_enumerable()
    {
        // given
        IEnumerable<int> source = Enumerable.Range(1, 3);

        // when
        var result = source.AsIReadOnlyList();

        // then
        result.Should().BeEquivalentTo([1, 2, 3]);
    }

    // AsDictionary tests

    [Fact]
    public void as_dictionary_should_return_same_instance_when_already_dictionary()
    {
        // given
        var source = new Dictionary<string, int>(StringComparer.Ordinal) { { "a", 1 } };

        // when
        var result = source.AsDictionary();

        // then
        result.Should().BeSameAs(source);
    }

    [Fact]
    public void as_dictionary_should_convert_idictionary_to_dictionary()
    {
        // given
        IDictionary<string, int> source = new SortedDictionary<string, int>(StringComparer.Ordinal)
        {
            { "a", 1 },
            { "b", 2 },
        };

        // when
        var result = source.AsDictionary();

        // then
        result.Should().BeOfType<Dictionary<string, int>>();
        result.Should().ContainKey("a").WhoseValue.Should().Be(1);
        result.Should().ContainKey("b").WhoseValue.Should().Be(2);
    }

    // JoinAsString tests

    [Fact]
    public void join_as_string_should_join_strings_with_separator()
    {
        // given
        IEnumerable<string> source = ["a", "b", "c"];

        // when
        var result = source.JoinAsString(", ");

        // then
        result.Should().Be("a, b, c");
    }

    [Fact]
    public void join_as_string_should_join_strings_with_char_separator()
    {
        // given
        IEnumerable<string> source = ["a", "b", "c"];

        // when
        var result = source.JoinAsString('-');

        // then
        result.Should().Be("a-b-c");
    }

    [Fact]
    public void join_as_string_should_join_objects_with_separator()
    {
        // given
        IEnumerable<int> source = [1, 2, 3];

        // when
        var result = source.JoinAsString(", ");

        // then
        result.Should().Be("1, 2, 3");
    }

    [Fact]
    public void join_as_string_should_join_objects_with_char_separator()
    {
        // given
        IEnumerable<int> source = [1, 2, 3];

        // when
        var result = source.JoinAsString('-');

        // then
        result.Should().Be("1-2-3");
    }

    [Fact]
    public void join_as_string_should_return_empty_for_empty_source()
    {
        // given
        IEnumerable<string> source = [];

        // when
        var result = source.JoinAsString(", ");

        // then
        result.Should().BeEmpty();
    }

    // WhereIf tests

    [Fact]
    public void where_if_should_filter_when_condition_true()
    {
        // given
        IEnumerable<int> source = [1, 2, 3, 4, 5];

        // when
        var result = source.WhereIf(true, x => x > 2);

        // then
        result.Should().BeEquivalentTo([3, 4, 5]);
    }

    [Fact]
    public void where_if_should_not_filter_when_condition_false()
    {
        // given
        IEnumerable<int> source = [1, 2, 3, 4, 5];

        // when
        var result = source.WhereIf(false, x => x > 2);

        // then
        result.Should().BeEquivalentTo([1, 2, 3, 4, 5]);
    }

    [Fact]
    public void where_if_with_index_should_filter_when_condition_true()
    {
        // given
        IEnumerable<int> source = [1, 2, 3, 4, 5];

        // when
        var result = source.WhereIf(true, (x, i) => i >= 2);

        // then
        result.Should().BeEquivalentTo([3, 4, 5]);
    }

    [Fact]
    public void where_if_with_index_should_not_filter_when_condition_false()
    {
        // given
        IEnumerable<int> source = [1, 2, 3, 4, 5];

        // when
        var result = source.WhereIf(false, (x, i) => i >= 2);

        // then
        result.Should().BeEquivalentTo([1, 2, 3, 4, 5]);
    }

    // HasDuplicates tests

    [Fact]
    public void has_duplicates_should_return_true_when_duplicates_exist()
    {
        // given
        var source = new[] { new { Id = 1 }, new { Id = 2 }, new { Id = 1 } };

        // when
        var result = source.HasDuplicates(x => x.Id);

        // then
        result.Should().BeTrue();
    }

    [Fact]
    public void has_duplicates_should_return_false_when_no_duplicates()
    {
        // given
        var source = new[] { new { Id = 1 }, new { Id = 2 }, new { Id = 3 } };

        // when
        var result = source.HasDuplicates(x => x.Id);

        // then
        result.Should().BeFalse();
    }

    [Fact]
    public void has_duplicates_should_return_false_for_empty_source()
    {
        // given
        var source = Array.Empty<int>();

        // when
        var result = source.HasDuplicates(x => x);

        // then
        result.Should().BeFalse();
    }

    // ToListAsync (Task) tests

    [Fact]
    public async Task to_list_async_from_task_should_materialize_result()
    {
        // given
        Task<IEnumerable<int>> task = Task.FromResult<IEnumerable<int>>([1, 2, 3]);

        // when
        var result = await task.ToListAsync();

        // then
        result.Should().BeOfType<List<int>>();
        result.Should().BeEquivalentTo([1, 2, 3]);
    }

    // ToArrayAsync (Task) tests

    [Fact]
    public async Task to_array_async_from_task_should_materialize_result()
    {
        // given
        Task<IEnumerable<int>> task = Task.FromResult<IEnumerable<int>>([1, 2, 3]);

        // when
        var result = await task.ToArrayAsync();

        // then
        result.Should().BeOfType<int[]>();
        result.Should().BeEquivalentTo([1, 2, 3]);
    }

    // AsEnumerableOnce tests

    [Fact]
    public void as_enumerable_once_should_allow_single_enumeration()
    {
        // given
        IEnumerable<int> source = [1, 2, 3];
        var once = source.AsEnumerableOnce();

        // when
        var result = once.ToList();

        // then
        result.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public void as_enumerable_once_should_throw_on_second_enumeration()
    {
        // given
        IEnumerable<int> source = [1, 2, 3];
        var once = source.AsEnumerableOnce();

        // when
        _ = once.ToList(); // first enumeration
        var act = () => once.ToList(); // second enumeration

        // then
        act.Should().Throw<InvalidOperationException>().WithMessage("*already enumerated*");
    }
}
