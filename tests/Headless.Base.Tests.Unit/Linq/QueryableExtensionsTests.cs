// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;

namespace Tests.Linq;

public sealed class QueryableExtensionsTests
{
    private sealed record QueryableTestEntity(int Id, string Name);

    private readonly List<QueryableTestEntity> _queryableTestEntities =
    [
        new(1, "Headless"),
        new(2, "Charities"),
        new(3, "Storm"),
    ];

    [Fact]
    public void where_if_should_apply_filter_when_condition_is_true_with_real_query()
    {
        // given
        var query = _queryableTestEntities.AsQueryable();
        Expression<Func<QueryableTestEntity, bool>> predicate = entity => entity.Name.Contains('a');

        // when
        var result = query.WhereIf(true, predicate);

        // then
        result.Should().HaveCount(2);
        result.Select(e => e.Name).Should().BeEquivalentTo("Headless", "Charities");
    }

    [Fact]
    public void where_if_should_select_only_specific_fields_when_condition_is_true()
    {
        // given
        var query = _queryableTestEntities.AsQueryable();
        Expression<Func<QueryableTestEntity, bool>> predicate = entity => entity.Id > 1;

        // when
        var result = query.WhereIf(true, predicate).Select(e => e.Name).ToList();

        // then
        result.Should().HaveCount(2);
        result.Should().BeEquivalentTo("Zad-charities", "Storm");
    }

    [Fact]
    public void where_if_should_not_apply_filter_but_return_sorted_results()
    {
        // given
        var query = _queryableTestEntities.AsQueryable();
        Expression<Func<QueryableTestEntity, bool>> predicate = entity => entity.Id > 1;

        // when
        var result = query.WhereIf(false, predicate).OrderBy(e => e.Name).ToList();

        // then
        result.Should().HaveCount(3);
        result.Select(e => e.Name).Should().BeEquivalentTo("Headless", "Storm", "Charities");
    }
}
