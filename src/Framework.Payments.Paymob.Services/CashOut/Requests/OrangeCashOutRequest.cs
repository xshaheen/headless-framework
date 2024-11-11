namespace Framework.Payments.Paymob.Services.CashOut.Requests;

public sealed record OrangeCashOutRequest(decimal Amount, string PhoneNumber, string FullName);
