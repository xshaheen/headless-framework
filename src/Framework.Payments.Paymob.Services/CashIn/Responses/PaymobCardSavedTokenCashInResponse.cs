// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Payments.Paymob.Services.CashIn.Responses;

public sealed record PaymobCardSavedTokenCashInResponse
{
    public required int TransactionId { get; init; }

    public required string OrderId { get; init; }

    public required bool Is3DSecure { get; init; }

    public required bool IsSuccess { get; init; }

    public required string? RedirectionUrl { get; init; }
}
