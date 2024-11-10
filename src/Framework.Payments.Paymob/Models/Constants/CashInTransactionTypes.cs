// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

namespace Framework.Payments.Paymob.CashIn.Models.Constants;

public static class CashInTransactionTypes
{
    public const string Auth = "auth";
    public const string Capture = "capture";
    public const string Type3ds = "3ds";
    public const string Refund = "refund";
    public const string Standalone = "standalone";
    public const string Void = "void";
}
