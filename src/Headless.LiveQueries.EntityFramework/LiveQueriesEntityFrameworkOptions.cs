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
}

internal sealed class LiveQueriesEntityFrameworkOptionsValidator : AbstractValidator<LiveQueriesEntityFrameworkOptions>
{
    public LiveQueriesEntityFrameworkOptionsValidator()
    {
        RuleFor(x => x.MaxCachedCommands).GreaterThan(0);
    }
}
