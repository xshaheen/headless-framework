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

        var textBytes = Encoding.UTF8.GetBytes(concatenatedString);
        var keyBytes = Encoding.UTF8.GetBytes(_options.Hmac);
        var hashBytes = _GetHashBytes(textBytes, keyBytes);
        var lowerCaseHexHash = _ToLowerCaseHex(hashBytes);

        return lowerCaseHexHash.Equals(hmac, StringComparison.Ordinal);
    }

    private static string _ToLowerCaseHex(byte[] hashBytes)
    {
        var sb = new StringBuilder(hashBytes.Length * 2);

        foreach (var b in hashBytes)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{b:x2}");
        }

        return sb.ToString();
    }

    private static byte[] _GetHashBytes(byte[] textBytes, byte[] keyBytes)
    {
        using var hash = new HMACSHA512(keyBytes);

        return hash.ComputeHash(textBytes);
    }
}
