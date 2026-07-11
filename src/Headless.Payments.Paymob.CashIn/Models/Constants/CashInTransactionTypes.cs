// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Payments.Paymob.CashIn.Models.Constants;

/// <summary>Paymob Accept transaction-type discriminators as they appear on transaction and callback payloads.</summary>
[PublicAPI]
public static class CashInTransactionTypes
{
    /// <summary>An authorization hold placed on the card without capturing funds.</summary>
    public const string Auth = "auth";

    /// <summary>A capture of previously authorized funds.</summary>
    public const string Capture = "capture";

    /// <summary>A 3-D Secure authentication step.</summary>
    public const string Type3Ds = "3ds";

    /// <summary>A refund of a previously captured transaction.</summary>
    public const string Refund = "refund";

    /// <summary>A standalone (single-step auth-and-capture) transaction.</summary>
    public const string Standalone = "standalone";

    /// <summary>A void of a same-day transaction.</summary>
    public const string Void = "void";
}
