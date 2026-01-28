// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.Services.CashIn.Models;

public static class PaymobTransactionResponseCodes
{
    /// <summary>
    /// The transaction has been processed successfully
    /// </summary>
    public const int TransactionApproved = 0;

    /// <summary>
    /// The Cardholder issuer has indicated there is a problem with the credentials/card number used in the transaction.
    /// The Cardholder should use an alternate Card/payment method, or contact their bank to resolve this issue.
    /// </summary>
    public const int ReferToIssuer = 1;

    /// <summary>
    /// The Cardholders issuer has indicated there is a problem with the card number.
    /// The Cardholder should use an alternate payment method, or contact their bank.
    /// </summary>
    public const int ReferToIssuerSpecial = 2;

    /// <summary>
    /// This error indicates that either the Merchant facility is non-functional or the details entered into the Gateway
    /// are invalid. Reasons for an Invalid Merchant ID Error Code.
    /// The disconnection between your merchant account and payment gateway may be caused by a bad gateway configuration,
    /// bank error, improper card configuration, or terminated merchant account.
    /// Please reach out to our Customer support for any further support in this regard.
    /// </summary>
    public const int InvalidMerchantOrServiceProvider = 3;

    /// <summary>
    /// The issuing bank of the customer has declined the transaction as the card may have been reported as stolen or lost.
    /// Please advise cardholder to contact their issuing bank for the resolution of this error.
    /// </summary>
    public const int PickUpCard = 4;

    /// <summary>
    /// Do not error comes when the customer's issuing bank is not authorising this transaction due to any valid reason. The payment has been declined by your bank.
    /// <list type="bullet">
    /// <item>The bank's fraud rules (which consider various factors that are not made public) have been triggered.</item>
    /// <item>The bank may have placed a temporary hold on the customer's card.</item>
    /// <item>The purchase session may have been locked due to multiple declined payments.</item>
    /// <item>The seller is located in a country different from that of the card issuing bank.</item>
    /// </list>
    ///
    /// This can be resolved by following below steps:
    /// <list type="number">
    /// <item>Ask the customer to contact their bank, explaining that they are trying to process a payment. The customer can ask the bank to allow the payment.</item>
    /// <item>Ask the customer to try again at a later time. The issuing bank may have only placed a temporary hold on the card.</item>
    /// <item>Ask the customer to use an alternative credit card.</item>
    /// </list>
    /// </summary>
    public const int DoNotHonour = 5;

    /// <summary>
    /// The Cardholders issuer has declined the transaction as there is a problem with the card number.
    /// The Cardholder should contact their card issuer and/or use an alternate payment method.
    /// </summary>
    public const int Error = 6;

    /// <summary>
    /// The Cardholders card issuer has declined the transaction and requested that the card be
    /// retained as the card may have been reported as lost or stolen.
    /// </summary>
    public const int PickupCardSpecialCondition = 7;

    /// <summary>
    /// Transaction processed successfully - identification NOT required. This code is returned by some
    /// banks in place of 00 response. This means your transaction has been approved.
    /// </summary>
    public const int HonourWithIdentification = 8;

    /// <summary>
    /// The Cardholders issuer has indicated there is a problem with the card number.
    /// The Cardholder should contact their bank and/or use an alternate payment method.
    /// </summary>
    public const int RequestInProgress = 9;

    /// <summary>
    /// The transaction was successful for a partial amount.
    /// </summary>
    public const int ApprovedForPartialAmount = 10;

    /// <summary>
    /// The bank has declined the transaction because of an invalid format or field. This indicates the card details were incorrect.
    /// Check card data entered and try again. This code is often returned from the issuer when they do not accept the transaction.
    /// This can possibly be when a transaction for the same amount and merchant is attempted multiple times quickly for the same card.
    /// The cardholder should contact their issuing bank.
    /// </summary>
    public const int InvalidTransaction = 12;

    /// <summary>
    /// The Cardholders issuer has declined the transaction because of an invalid format or field; or amount exceeds
    /// maximum for card program. This usually is the result of a typo (negative amount or ineligible symbol).
    /// Double-check what you entered and make sure it wasn't negative or included incorrect symbols.
    /// </summary>
    public const int InvalidAmount = 13;

    /// <summary>
    /// The Cardholders issuing bank has declined the transaction as the payment card number is incorrectly entered,
    /// or does not exist. This indicates a problem with the information entered for the card. Double-check the card number, expiration date, and CVV.
    /// If you haven't already, also ensure that the card has been activated.
    /// </summary>
    public const int InvalidCardNumber = 14;

    /// <summary>
    /// The Cardholders issuer does not exist.
    /// Check the card information and try processing the transaction again. Wrong card number (Codes 14 and 15):
    /// There are two wrong ways to enter the card number improperly.
    /// If the very first digit is incorrect, you'll see error code 15 for "no such issuers" since the first digit pinpoints the card's issuing bank.
    /// If we say more specifically, The card number entered is wrong since it does not start with a 3 (AMEX), 4 (Visa), 5 (MasterCard), or 6 (Discover).
    /// </summary>
    public const int NoIssuer = 15;

    /// <summary>
    /// An unspecified bank error has occurred. The Cardholder should attempt to process the transaction again.
    /// </summary>
    public const int ApprovedUpdateTrack3 = 16;

    /// <summary>
    /// This indicates that the transaction was authorised and subsequently voided.
    /// Voided transactions do not appear on the customer's statement or form part of your settlement total.
    /// </summary>
    public const int CustomerCancellation = 17;

    /// <summary>
    /// The Cardholders card issuer has prevented this transaction from taking place due to an ongoing or previous dispute.
    /// </summary>
    public const int CustomerDispute = 18;

    /// <summary>
    /// The transaction has not been processed and the Cardholder should attempt to process the transaction again.
    /// No further information is provided from the bank as to the reason why this was not processed.
    /// </summary>
    public const int ReenterLastTransaction = 19;

    /// <summary>
    /// The bank has declined the transaction because of an invalid format or field.
    /// This indicates the card details were incorrect. Check card data entered and try again.
    /// </summary>
    public const int InvalidResponseAcquirerError = 20;

    /// <summary>
    /// The Cardholders issuer has indicated there is a problem with the payment card number.
    /// The Cardholder should use an alternate payment method, or contact their bank.
    /// </summary>
    public const int NoActionTaken = 21;

    /// <summary>
    /// The Cardholders issuer could not be contacted during the transaction.
    /// The Cardholder should check the card information and try processing the transaction again.
    /// </summary>
    public const int SuspectedMalfunction = 22;

    /// <summary>
    /// An unspecified bank error has occurred. The Cardholder should attempt to process the transaction again.
    /// </summary>
    public const int UnacceptableTransaction = 23;

    /// <summary>
    /// An unspecified bank error has occurred. The Cardholder should attempt to process the transaction again.
    /// </summary>
    public const int FileUpdateImpossible = 24;

    /// <summary>
    /// The Cardholders card issuer does not recognise the credit card details.
    /// The Cardholder should check the card information and try processing the transaction again.
    /// </summary>
    public const int UnableToLocateRecordOnFile = 25;

    /// <summary>
    /// The Cardholders card issuer does not recognise the credit card details.
    /// The Cardholder should check the card information and try processing the transaction again.
    /// </summary>
    public const int DuplicateReferenceNumber = 26;

    /// <summary>
    /// The Cardholders card issuer does not recognise the credit card details.
    /// The Cardholder should check the card information and try processing the transaction again.
    /// </summary>
    public const int ErrorInReferenceNumber = 27;

    /// <summary>
    /// An unspecified bank error has occurred. The Cardholder should attempt to process the transaction again.
    /// A code 28 error happens during this initial authorization process.
    /// It simply means there was an issue immediately retrieving the information from the card-issuing bank.
    /// Therefore, because of its temporary status, the transaction should be put through again.
    /// </summary>
    public const int FileIsTemporarilyUnavailableForUpdate = 28;

    /// <summary>
    /// An unspecified bank error has occurred. The Cardholder should attempt to process the transaction again.
    /// </summary>
    public const int FileActionFailedContactAcquirer = 29;

    /// <summary>
    /// The Cardholders issuer does not recognise the transaction details being entered. This is due to a format error.
    /// The Cardholder should check the transaction information and try processing the transaction again.
    /// We can further explain this as An "Invalid format" error when updating your credit card means that the credit
    /// card number was entered with spaces, dashes, or some other character that is not allowed.
    /// </summary>
    public const int FormatError = 30;
}
