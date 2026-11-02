// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Linq.Expressions;
using Headless.Checks;
using Headless.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;

namespace System.Linq;

/// <summary>Filters an EF Core query by an Arabic search key.</summary>
[PublicAPI]
public static class HeadlessArabicSearchQueryableExtensions
{
    // The key and term slots are swapped for the caller's key selector body and the captured term.
    private static readonly Expression<Func<string?, string?, bool>> _Match = (key, term) =>
        EF.Functions.Like(key, "%" + ArabicSearchFunctions.Key(term) + "%")
        && ArabicSearchFunctions.Key(term)!.Length > 0;

    /// <summary>
    /// Keeps the rows whose stored search key contains the search key of <paramref name="term" />. The database
    /// normalizes the term with the function that built the stored keys, so a spelling variant matches:
    /// <c>احمد</c> finds <c>أحمد علي</c>.
    /// </summary>
    /// <typeparam name="T">The entity type.</typeparam>
    /// <param name="query">The query to filter.</param>
    /// <param name="searchKey">The stored search key column, declared with <c>HasArabicSearchKey</c>.</param>
    /// <param name="term">The text the user searched for.</param>
    /// <returns>The filtered query.</returns>
    /// <remarks>
    /// <para>
    /// A term with no searchable character (<c>"!!!"</c>, only tashkeel, or <see langword="null" />) matches no rows.
    /// Its key is empty, and a bare <c>LIKE '%%'</c> would match every row.
    /// </para>
    /// <para>
    /// The match is a <c>LIKE</c> that the key's GIN trigram index serves. A term whose key is shorter than three
    /// characters gives the index nothing to look up, so the database scans the table; require a minimum length
    /// where the table is large. Usable only in a model that calls <c>HasHeadlessArabicSearch</c>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <c>db.People.WhereArabicSearchMatches(p => p.NameSearch, term).ToListAsync(cancellationToken)</c>
    /// </example>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="query" /> or <paramref name="searchKey" /> is <see langword="null" />.
    /// </exception>
    [MustUseReturnValue]
    public static IQueryable<T> WhereArabicSearchMatches<T>(
        this IQueryable<T> query,
        Expression<Func<T, string?>> searchKey,
        string? term
    )
    {
        Argument.IsNotNull(query);
        Argument.IsNotNull(searchKey);

        // A captured variable, not a constant: EF Core sends it as a parameter, so every term shares one plan.
        Expression<Func<string?>> termAccess = () => term;

        var body = ReplacingExpressionVisitor.Replace(
            [_Match.Parameters[0], _Match.Parameters[1]],
            [searchKey.Body, termAccess.Body],
            _Match.Body
        );

        return query.Where(Expression.Lambda<Func<T, bool>>(body, searchKey.Parameters));
    }
}
