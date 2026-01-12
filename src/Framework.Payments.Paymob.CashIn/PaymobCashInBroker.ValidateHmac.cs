// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Framework.Checks;
using Framework.Payments.Paymob.CashIn.Models.Callback;

namespace Framework.Payments.Paymob.CashIn;

public partial class PaymobCashInBroker
{
    public bool Validate(CashInCallbackQueryParameters queryParameters)
    {
        return Validate(queryParameters.ToConcatenatedString(), queryParameters.Hmac);
    }

    public bool Validate(CashInCallbackTransaction transaction, string hmac)
    {
        return Validate(transaction.ToConcatenatedString(), hmac);
    }

    public bool Validate(CashInCallbackToken token, string hmac)
    {
        return Validate(token.ToConcatenatedString(), hmac);
    }

    public bool Validate(string concatenatedString, string hmac)
    {
        Argument.IsNotNullOrEmpty(concatenatedString);
        Argument.IsNotNullOrEmpty(hmac);

        var keyBytes = Encoding.UTF8.GetBytes(_options.Hmac);
        var textBytes = Encoding.UTF8.GetBytes(concatenatedString);
        var hashBytes = HMACSHA512.HashData(keyBytes, textBytes);
        var computedHmac = Convert.ToHexStringLower(hashBytes);

        var computedBytes = Encoding.UTF8.GetBytes(computedHmac);
        var providedBytes = Encoding.UTF8.GetBytes(hmac);

        return CryptographicOperations.FixedTimeEquals(computedBytes, providedBytes);
    }
}
