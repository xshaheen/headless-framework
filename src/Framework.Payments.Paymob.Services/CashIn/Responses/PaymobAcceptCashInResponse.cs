namespace Framework.Payments.Paymob.Services.CashIn.Responses;

public sealed record PaymobAcceptCashInResponse(string BillingReference, string OrderId, int Expiration);
