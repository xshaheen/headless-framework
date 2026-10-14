// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Payments.Paymob.CashIn;

namespace Tests;

public sealed class CashInOrdersPageRequestTests
{
    [Theory]
    [InlineData(true, "true")]
    [InlineData(false, "false")]
    public void should_send_the_live_filter_as_is_live(bool isLive, string expected)
    {
        // when
        var request = CashInOrdersPageRequest.Create.WithIsLive(isLive);

        // then
        request.Query.Should().ContainKey("is_live").WhoseValue.Should().Be(expected);
        request.Query.Should().NotContainKey("transaction_id");
    }
}
