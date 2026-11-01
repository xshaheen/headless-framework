// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Search;

/// <summary>
/// The Arabic search function as an EF Core query function. <c>modelBuilder.HasHeadlessArabicSearch(version)</c> maps
/// <see cref="Key" /> to that version's database function, so a query term is normalized by the database, with the same
/// rules that built the stored keys.
/// </summary>
[PublicAPI]
public static class ArabicSearchFunctions
{
    /// <summary>
    /// Translates to the mapped database function, for example <c>headless.ar_norm_v1(@term)</c>. Usable only inside
    /// an EF Core query.
    /// </summary>
    /// <param name="value">The text to normalize.</param>
    /// <returns>The search key, as the database computes it.</returns>
    /// <remarks>
    /// For the common search, call <c>WhereArabicSearchMatches</c> instead of writing
    /// <c>EF.Functions.Like(p.NameSearch, "%" + ArabicSearchFunctions.Key(term) + "%")</c>: a term with no searchable
    /// character (<c>"!!!"</c>, only tashkeel, <see langword="null" />) makes that pattern <c>%%</c>, which matches
    /// every row, and the helper guards against it.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Always, when called outside a query: evaluating it in .NET would let a different implementation stand in for the
    /// database's. Use <see cref="ArabicSearchNormalizer" /> for in-memory work.
    /// </exception>
    public static string? Key(string? value)
    {
        throw new InvalidOperationException(
            $"{nameof(ArabicSearchFunctions)}.{nameof(Key)} runs only inside an EF Core query whose model calls "
                + "HasHeadlessArabicSearch. Use ArabicSearchNormalizer.Normalize in memory."
        );
    }
}
