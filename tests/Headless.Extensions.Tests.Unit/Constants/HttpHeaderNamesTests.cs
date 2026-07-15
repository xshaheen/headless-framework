// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Constants;

namespace Tests.Constants;

public sealed class HttpHeaderNamesTests
{
    [Fact]
    public void should_have_expected_value_when_idempotency_key()
    {
        HttpHeaderNames.IdempotencyKey.Should().Be("Idempotency-Key");
    }

    [Fact]
    public void should_have_expected_value_when_idempotent_replayed()
    {
        HttpHeaderNames.IdempotentReplayed.Should().Be("Idempotent-Replayed");
    }
}
