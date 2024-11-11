namespace Framework.Payments.Paymob.Services.CashOut.Responses;

public sealed record CashOutResponse(string TransactionId, CashOutResponseStatus Status);
