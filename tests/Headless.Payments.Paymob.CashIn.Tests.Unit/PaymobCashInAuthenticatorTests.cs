// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Tests;

public sealed partial class PaymobCashInAuthenticatorTests(PaymobCashInFixture fixture)
    : IClassFixture<PaymobCashInFixture>
{
    protected override Faker Faker { get; set; } = CashInTestData.CreateFaker();
}
