// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Models;

/// <summary>
/// Paymob has its own Fraud management system to protect its Merchants from Fraudulent Transactions.
/// </summary>
public static class PaymobRiskDeclineCodes
{
    /// <summary>
    /// You will receive 111 Risk decline if you are performing a transaction from Country IP which is not in whitelist. Kindly coordinate with Paymob support team for any further clarity.
    /// </summary>
    public const int IpCountryNotInWhitelist = 111;

    /// <summary>
    /// You will receive 112 Risk decline if you are performing a transaction from Country IP which is in Blacklist. Kindly coordinate with Paymob support team for any further clarity.
    /// </summary>
    public const int IpCountryInBlacklist = 112;

    /// <summary>
    /// You will receive 113 Risk decline if you are performing a transaction from "IP" which is not in Whitelist. Kindly coordinate with Paymob support team for any further clarity.
    /// </summary>
    public const int IpNotInWhitelist = 113;

    /// <summary>
    /// you will receive 113 Risk decline if you are performing a transaction from "IP" which is in Blacklist. Kindly coordinate with Paymob support team for any further clarity.
    /// </summary>
    public const int IpInBlacklist = 114;

    /// <summary>
    /// This means that you are performing a transaction from the bank card number , which bin country is not in White list in our FMS (Fraud Management System)
    /// </summary>
    public const int BinCountryNotInWhitelist = 121;

    /// <summary>
    /// This means that you are performing a transaction from the bank card number , which bin country is in black list in our FMS (Fraud Management System)
    /// </summary>
    public const int BinCountryInBlacklist = 122;

    /// <summary>
    /// This means that you are performing a transaction from the bank card number , which bin country is in black list in our FMS (Fraud Management System)
    /// </summary>
    public const int BinNotInWhitelist = 123;

    /// <summary>
    /// This transacton is getting declined due to Bin is not in whitelist.
    /// </summary>
    public const int BinInBlacklist = 124;

    /// <summary>
    /// This transaction is getting declined due to amount limit exceed.
    /// </summary>
    public const int AmountCentsExceedsAllowed = 131;

    /// <summary>
    /// This transaction is getting declined due to amount limit exceed.
    /// </summary>
    public const int AmountCentsExceedsMotoLimit = 141;

    /// <summary>
    /// This means that the email server could not find the DNS record for the entered email address in the invoice. This can happen for a number of reasons, such as: The recipient's email address is incorrect or contains a typo.
    /// </summary>
    public const int EmailDnsCheckFailed = 153;

    /// <summary>
    /// This is due to disposable email addresses often being associated with fraudulent activity. For example, scammers may use disposable email addresses to create fake accounts or to sign up for services without paying. We may reject payments from disposable email addresses because they are difficult to verify. When making an online payment, we typically needs to verify your identity and your email address. This is to help prevent fraud and to the user financial information.
    /// </summary>
    public const int EmailIsDisposable = 154;

    /// <summary>
    /// IP address is in a global blacklist, it means that the IP address has been flagged as a potential source of malicious activity. This can happen for a number of reasons, such as: IP address has been used to send spam or phishing emails. IP address has been used to launch DDoS attacks. IP address has been infected with malware. IP address has been used to commit other types of cybercrime.
    /// </summary>
    public const int IpIsInGlobalBlacklist = 201;

    /// <summary>
    /// This is due having the email addresses linked to the attempted transactions identified as being associated with spam, phishing, Fraudulent attempts or other malicious activities. You will need to reach out to Pyamob support team directly for further support and help.
    /// </summary>
    public const int EmailInGlobalBlacklist = 202;

    /// <summary>
    /// This means that this transaction has been declined due to Risk Checks and our FMS System has rejected this transaction due to any Fraud management reasons like you are not passing the genuine or unique data i.e Email, phone or Name. To avoid this error please always try to pass genuine customer data in the request parameters.
    /// </summary>
    public const int FraudEngine = 301;

    /// <summary>
    /// This error code means that you are trying to pass test credentials upon live integration ids.
    /// </summary>
    public const int IfMerchantIsLiveAndUsingTestCredentials = 200;
}
