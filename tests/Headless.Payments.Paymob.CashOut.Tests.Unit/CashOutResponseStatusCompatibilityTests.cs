// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.Services.CashOut.Responses;

namespace Tests;

public sealed class CashOutResponseStatusCompatibilityTests
{
    [Fact]
    public void should_keep_cash_out_response_status_numeric_contract_stable()
    {
        new[] { (int)CashOutResponseStatus.Pending, (int)CashOutResponseStatus.Success }.Should().Equal(0, 1);
    }
}
