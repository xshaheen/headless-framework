// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.LiveQueries;

/// <summary>Settings of live-query tracking for one <c>DbContext</c> type.</summary>
[PublicAPI]
public sealed class LiveQueriesEntityFrameworkOptions
{
    /// <summary>
    /// Gets or sets how many distinct SQL command texts the tracker remembers the tables of, per model. A command
    /// whose text is remembered costs one lookup; any other is searched for every mapped table name. Past the limit
    /// the memory is emptied and fills again, so a low limit costs repeated searches and never a missed table.
    /// Default: 1,024.
    /// </summary>
    public int MaxCachedCommands { get; set; } = 1024;

    /// <summary>
    /// Gets or sets whether a saved row re-runs only the live queries whose filters the row can pass, instead of
    /// every live query that read the row's table. A filter narrows a query only when it compares a primary-key
    /// property, or a property that cannot change after insert, for equality with a constant or a captured value of
    /// an integer, <see cref="bool" />, enum or <see cref="Guid" /> type; every other query and every other kind of
    /// write keeps matching by table, so turning this on can spare re-runs and never loses one. It adds a comment to
    /// the SQL of the queries it narrows and a step to every entity the context materializes. Default:
    /// <see langword="false" />.
    /// </summary>
    public bool RowLevelMatching { get; set; }
}

internal sealed class LiveQueriesEntityFrameworkOptionsValidator : AbstractValidator<LiveQueriesEntityFrameworkOptions>
{
    public LiveQueriesEntityFrameworkOptionsValidator()
    {
        RuleFor(x => x.MaxCachedCommands).GreaterThan(0);
    }
}
