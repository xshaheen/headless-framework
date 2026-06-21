// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Callback;

/// <summary>
/// A normalised view of the card used in a Paymob transaction, extracted from raw callback or
/// transaction data.
/// </summary>
/// <param name="CardNumber">The masked card number (PAN) or a placeholder such as <c>xxxx</c> when unavailable.</param>
/// <param name="Type">The normalised card scheme name (e.g., <c>Visa</c>, <c>MasterCard</c>), or <see langword="null"/> when unknown.</param>
/// <param name="Bank">The issuing bank name, or <see langword="null"/> when not provided by Paymob.</param>
public sealed record CashInCardInfo(string CardNumber, string? Type, string? Bank);
