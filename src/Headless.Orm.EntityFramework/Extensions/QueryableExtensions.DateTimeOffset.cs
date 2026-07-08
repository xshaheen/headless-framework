// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using System.Reflection;
using Headless.Checks;
using Headless.EntityFramework;
using Headless.Linq;

#pragma warning disable IDE0130 // ReSharper disable once CheckNamespace
namespace Microsoft.EntityFrameworkCore;

public static partial class QueryableExtensions
{
    /// <summary>
    /// Count number of entities per each month in the interval from <paramref name="start"/>
    /// to <paramref name="end"/> months backward.
    /// </summary>
    /// <remarks>
    /// The day of <paramref name="start"/> and <paramref name="end"/> will be ignored
    /// and assume that the two is at the same day.
    /// </remarks>
    public static async Task<IEnumerable<EntityPerDateTimeOffset>> CountPerMonthAsync<T>(
        this IQueryable<T> queryable,
        Expression<Func<T, DateTimeOffset>> propSelector,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken token = default
    )
    {
        Argument.IsNotNull(queryable);
        Argument.Range(start, end);

        end = end.ToOffset(start.Offset);

        // Assuming the day of the month is irrelevant (i.e. the diff between 2020.1.1 and 2019.12.31 is one month also)
        var months = ((end.Year - start.Year) * 12) + end.Month - start.Month;

        var last = new DateTimeOffset(end.Year, end.Month, 1, 0, 0, 0, start.Offset);
        var first = last.AddMonths(-months);

        var entityType = typeof(T);
        var dateType = typeof(DateTimeOffset);
        var typeParameter = Expression.Parameter(entityType, "e");

        // Property accessor expressions
        var selectedPropertyInfo = (PropertyInfo)((MemberExpression)propSelector.Body).Member;
        var propertyAccessor = Expression.Property(typeParameter, selectedPropertyInfo);

        // Greater than start Predicate
        var startMonthBefore = start.AddMonths(-1);
        var startConstant = Expression.Constant(startMonthBefore, dateType);
        var greaterThanStartExpression = Expression.GreaterThan(left: propertyAccessor, right: startConstant);
        var greaterThanStartPredicate = Expression.Lambda<Func<T, bool>>(greaterThanStartExpression, typeParameter);

        // Less than end Predicate
        var lastMonthLater = last.AddMonths(1);
        var endConstant = Expression.Constant(lastMonthLater);
        var lessThanEndExpression = Expression.LessThan(left: propertyAccessor, right: endConstant);
        var lessThanEndPredicate = Expression.Lambda<Func<T, bool>>(lessThanEndExpression, typeParameter);

        // Query
        var predicate = greaterThanStartPredicate.And(lessThanEndPredicate);

        var filteredDates = queryable.Where(predicate).Select(propSelector);

        try
        {
            var counts = await filteredDates
                .GroupBy(static date => new { date.Year, date.Month })
                .Select(static group => new
                {
                    group.Key.Year,
                    group.Key.Month,
                    Count = group.Count(),
                })
                .ToDictionaryAsync(
                    x => new DateTimeOffset(x.Year, x.Month, 1, 0, 0, 0, start.Offset),
                    static x => x.Count,
                    cancellationToken: token
                )
                .ConfigureAwait(false);

            return from n in Enumerable.Range(1, months)
                let month = first.AddMonths(n)
                select new EntityPerDateTimeOffset(month, counts.GetValueOrDefault(month));
        }
        catch (InvalidOperationException exception) when (_IsQueryTranslationFailure(exception))
        {
            var query = filteredDates.Select(date => new
            {
                At = date,
                Month = new DateTimeOffset(date.Year, date.Month, 1, 0, 0, 0, start.Offset),
            });

            var lookup = await query
                .ToLookupAsync(x => x.Month, x => x.At, cancellationToken: token)
                .ConfigureAwait(false);

            return from n in Enumerable.Range(1, months)
                let month = first.AddMonths(n)
                select new EntityPerDateTimeOffset(month, lookup[month].Count());
        }
    }
}
