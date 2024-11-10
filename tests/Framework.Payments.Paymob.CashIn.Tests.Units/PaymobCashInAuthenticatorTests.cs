// Copyright (c) Mahmoud Shaheen, 2021. All rights reserved.

namespace Tests;

public partial class PaymobCashInAuthenticatorTests : IClassFixture<PaymobCashInFixture>
{
    private readonly PaymobCashInFixture _fixture;

    public PaymobCashInAuthenticatorTests(PaymobCashInFixture fixture) => _fixture = fixture;
}
