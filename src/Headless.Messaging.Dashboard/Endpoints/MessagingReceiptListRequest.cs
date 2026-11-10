// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FluentValidation;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The query of the operator ledger: <c>?actor=&amp;targetKind=&amp;operationType=&amp;from=&amp;before=&amp;after=&amp;limit=</c>.
/// </summary>
/// <remarks><c>after</c> is the <c>nextCursor</c> an earlier page returned.</remarks>
internal sealed record MessagingReceiptListRequest(
    string? Actor,
    string? TargetKind,
    string? OperationType,
    DateTimeOffset? From,
    DateTimeOffset? Before,
    string? After,
    int? Limit
)
{
    /// <summary>The member of <typeparamref name="TEnum"/> <paramref name="value"/> names, or <see langword="null"/> when it is empty or names none.</summary>
    public static TEnum? Parse<TEnum>(string? value)
        where TEnum : struct, Enum =>
        !string.IsNullOrEmpty(value)
        && Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
        && Enum.IsDefined(parsed)
            ? parsed
            : null;
}

/// <summary>Refuses a ledger query that names a target kind, an operation type, or a cursor the ledger does not know.</summary>
internal sealed class MessagingReceiptListRequestValidator : AbstractValidator<MessagingReceiptListRequest>
{
    /// <summary>The validator holds no state, so every ledger read shares one.</summary>
    public static MessagingReceiptListRequestValidator Instance { get; } = new();

    public MessagingReceiptListRequestValidator()
    {
        RuleFor(static request => request.TargetKind)
            .Must(static kind =>
                string.IsNullOrEmpty(kind)
                || MessagingReceiptListRequest.Parse<MessagingOperationTargetKind>(kind) is not null
            )
            .WithMessage(static request => $"'{request.TargetKind}' is not an operation target kind.");

        RuleFor(static request => request.OperationType)
            .Must(static type =>
                string.IsNullOrEmpty(type)
                || MessagingReceiptListRequest.Parse<MessagingOperationType>(type) is not null
            )
            .WithMessage(static request => $"'{request.OperationType}' is not an operation type.");

        RuleFor(static request => request.After)
            .Must(static after => ReceiptListCursor.TryParse(after, out _))
            .WithMessage("after is not a cursor this list returned.");
    }
}
