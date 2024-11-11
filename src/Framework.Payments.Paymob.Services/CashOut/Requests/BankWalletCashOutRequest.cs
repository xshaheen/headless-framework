namespace Framework.Payments.Paymob.Services.CashOut.Requests;

public sealed record BankWalletCashOutRequest(decimal Amount, string PhoneNumber, string FullName);
