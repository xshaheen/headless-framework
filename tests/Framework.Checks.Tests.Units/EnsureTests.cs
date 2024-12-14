// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public sealed class EnsureTests
{
    [Fact]
    public void ensure_true_and_false()
    {
        // given
        const bool condition = true;

        // when & then
        Ensure.True(condition);
        Ensure.False(!condition);
    }

    [Fact]
    public void should_throw_when_condition_is_reverse()
    {
        // given
        const bool condition = false;

        // when
        var actionTrue = () => Ensure.True(condition);
        var actionFalse = () => Ensure.False(!condition);

        // then
        actionTrue.Should().Throw<InvalidOperationException>();
        actionFalse.Should().Throw<InvalidOperationException>();
    }
}
