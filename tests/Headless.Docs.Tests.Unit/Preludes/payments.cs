// Copyright (c) Mahmoud Shaheen. All rights reserved.

// Stand-ins for the injected broker the payments guide's statement-level examples assume.

global using static PaymentsAmbient;
using Headless.Payments.Paymob.CashOut;

#pragma warning disable IDE1006 // Ambient members mirror the camelCase locals the examples use.
public static class PaymentsAmbient
{
    public static IPaymobCashOutBroker broker => null!;
}
