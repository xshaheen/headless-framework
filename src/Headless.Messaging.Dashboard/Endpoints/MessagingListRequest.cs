// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;
using Headless.Dashboard;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The query of a published or received list: <c>?store=&amp;status=&amp;terminal=&amp;retrying=&amp;consumer=&amp;name=&amp;fingerprint=&amp;held=&amp;limit=&amp;after=</c>.
/// </summary>
/// <remarks>
/// <c>retrying</c> selects failed rows with a retry pending, so it takes no <c>status</c> or <c>terminal</c>;
/// <c>after</c> is the <c>next</c> cursor an earlier page returned.
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
    int? Limit,
    string? After
);

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

        RuleFor(static request => request.After)
            .Must(static after => MergedListCursor.TryParse(after, out _))
            .WithMessage("after is not a cursor this list returned.");
    }
}
