// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.ComponentModel;
using FluentValidation;
using Headless.Dashboard;

namespace Headless.Messaging.Dashboard;

#pragma warning disable MA0048 // A topic file: the list query, the search body, and their validators are peers.

/// <summary>
/// The query of a published or received list:
/// <c>?store=&amp;status=&amp;terminal=&amp;retrying=&amp;consumer=&amp;name=&amp;fingerprint=&amp;held=&amp;lane=&amp;messageId=&amp;from=&amp;before=&amp;sort=&amp;direction=&amp;limit=&amp;after=</c>.
/// </summary>
/// <remarks>
/// <c>retrying</c> selects failed rows with a retry pending, so it takes no <c>status</c> or <c>terminal</c>;
/// <c>from</c> and <c>before</c> bound the added time, <c>before</c> exclusive; <c>after</c> is the <c>next</c> cursor
/// an earlier page of the same sort and filters returned.
/// </remarks>
internal sealed record MessagingListRequest(
    string? Store,
    string? Status,
    bool? Terminal,
    bool? Retrying,
    string? Consumer,
    string? Name,
    string? Fingerprint,
    bool? Held,
    string? Lane,
    string? MessageId,
    DateTimeOffset? From,
    DateTimeOffset? Before,
    string? Sort,
    string? Direction,
    int? Limit,
    string? After
)
{
    /// <summary>The sort key, the added time unless asked otherwise. Read after validation.</summary>
    public MessageListSortKey ReadSort() =>
        DashboardValidation.ParseEnum<MessageListSortKey>(Sort) ?? MessageListSortKey.Added;

    /// <summary>
    /// The direction. The list opens on the newest rows, so the added time reads descending unless asked otherwise; a
    /// name sort reads A to Z. Read after validation.
    /// </summary>
    public ListSortDirection ReadDirection() =>
        DashboardValidation.ParseEnum<ListSortDirection>(Direction)
        ?? (ReadSort() == MessageListSortKey.Added ? ListSortDirection.Descending : ListSortDirection.Ascending);
}

/// <summary>
/// The body of <c>POST published/search</c> and <c>POST received/search</c>: a list's filters and the text to search
/// for, which travels only here, never in a URL.
/// </summary>
/// <remarks>
/// <c>content</c> matches the stored envelope, headers and body, as a literal substring without regard to case;
/// <c>correlationId</c> matches the correlation id header exactly, without regard to case. Set either or both.
/// </remarks>
internal sealed record MessagingSearchRequest
{
    public string? Content { get; init; }

    public string? CorrelationId { get; init; }

    public string? Store { get; init; }

    public string? Status { get; init; }

    public bool? Terminal { get; init; }

    public bool? Retrying { get; init; }

    public string? Consumer { get; init; }

    public string? Name { get; init; }

    public string? Fingerprint { get; init; }

    public bool? Held { get; init; }

    public string? Lane { get; init; }

    public string? MessageId { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? Before { get; init; }

    public string? Sort { get; init; }

    public string? Direction { get; init; }

    public int? Limit { get; init; }

    public string? After { get; init; }

    /// <summary>The list filters of the search, without its text.</summary>
    public MessagingListRequest ToListRequest() =>
        new(
            Store,
            Status,
            Terminal,
            Retrying,
            Consumer,
            Name,
            Fingerprint,
            Held,
            Lane,
            MessageId,
            From,
            Before,
            Sort,
            Direction,
            Limit,
            After
        );
}

/// <summary>Refuses a list query whose filters contradict each other or name a status or cursor the list does not know.</summary>
internal sealed class MessagingListRequestValidator : AbstractValidator<MessagingListRequest>
{
    /// <summary>The validator holds no state, so every list shares one.</summary>
    public static MessagingListRequestValidator Instance { get; } = new();

    public MessagingListRequestValidator()
    {
        RuleFor(static request => request.Retrying)
            .Must(
                static (request, retrying) =>
                    retrying != true || (request.Terminal != true && string.IsNullOrEmpty(request.Status))
            )
            .WithMessage("retrying selects failed rows with a retry pending; it takes no status or terminal.");

        RuleFor(static request => request.Status)
            .Must(static status =>
                string.IsNullOrEmpty(status) || DashboardValidation.ParseEnum<StatusName>(status) is not null
            )
            .WithMessage(static request => $"'{request.Status}' is not a message status.");

        // Only the two lanes a list shows; a row of an unknown lane is never listed.
        RuleFor(static request => request.Lane)
            .Must(static lane =>
                string.IsNullOrEmpty(lane)
                || DashboardValidation.ParseEnum<MessageLane>(lane) is MessageLane.Bus or MessageLane.Queue
            )
            .WithErrorCode(DashboardValidation.EnumRule)
            .WithMessage("lane must be Bus or Queue.");

        RuleFor(static request => request.Before)
            .Must(static (request, before) => request.From is not { } from || before is not { } end || from < end)
            .WithMessage("before must be later than from.");

        RuleFor(static request => request.Sort)
            .Must(static sort =>
                string.IsNullOrEmpty(sort) || DashboardValidation.ParseEnum<MessageListSortKey>(sort) is not null
            )
            .WithErrorCode(DashboardValidation.EnumRule)
            .WithMessage("sort must be added or name.");

        RuleFor(static request => request.Direction)
            .Must(static direction =>
                string.IsNullOrEmpty(direction)
                || DashboardValidation.ParseEnum<ListSortDirection>(direction) is not null
            )
            .WithErrorCode(DashboardValidation.EnumRule)
            .WithMessage("direction must be ascending or descending.");

        RuleFor(static request => request.After)
            .Must(static after => MergedListCursor.TryParse(after, out _))
            .WithMessage("after is not a cursor this list returned.");
    }
}

/// <summary>Refuses a search with no text, or with text longer than a stored value could match.</summary>
internal sealed class MessagingSearchRequestValidator : AbstractValidator<MessagingSearchRequest>
{
    /// <summary>The longest content text a search takes.</summary>
    public const int MaxContentLength = 1000;

    /// <summary>The longest correlation id a search takes.</summary>
    public const int MaxCorrelationIdLength = 200;

    /// <summary>The validator holds no state, so every search shares one.</summary>
    public static MessagingSearchRequestValidator Instance { get; } = new();

    // The messages name the fields, never the text: a validation problem is served and may be logged.
    public MessagingSearchRequestValidator()
    {
        RuleFor(static request => request.Content)
            .Must(
                static (request, content) =>
                    !string.IsNullOrEmpty(content) || !string.IsNullOrEmpty(request.CorrelationId)
            )
            .WithErrorCode(DashboardValidation.RequiredRule)
            .WithMessage("A search needs content or correlationId.");

        RuleFor(static request => request.Content)
            .MaximumLength(MaxContentLength)
            .WithMessage($"content takes at most {MaxContentLength} characters.");

        RuleFor(static request => request.CorrelationId)
            .MaximumLength(MaxCorrelationIdLength)
            .WithMessage($"correlationId takes at most {MaxCorrelationIdLength} characters.");
    }
}

#pragma warning restore MA0048
