namespace Framework.Payments.Paymob.Services.CashIn.Models;

public static class FailedCashInTransactionErrors
{
    public const string UnknownError = "UnknownError";
    public const string InsufficientFund = "InsufficientFunds";
    public const string AuthenticationFailed = "AuthenticationFailed";
    public const string Declined = "Declined";
    public const string RiskChecks = "RiskChecks";
}
