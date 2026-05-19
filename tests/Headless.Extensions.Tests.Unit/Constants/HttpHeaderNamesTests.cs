// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;

namespace Tests.Constants;

public sealed class HttpHeaderNamesTests
{
    [Fact]
    public void idempotency_key_should_have_expected_value()
    {
        HttpHeaderNames.IdempotencyKey.Should().Be("X-Idempotency-Key");
    }

    [Fact]
    public void idempotent_replayed_should_have_expected_value()
    {
        HttpHeaderNames.IdempotentReplayed.Should().Be("Idempotent-Replayed");
    }
}
