// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;

namespace Tests;

public sealed class EnsureTests
{
    [Fact]
    public void ensure_true_and_false()
    {
        // given
        bool condition = true;

        // when & then
        Ensure.True(condition);
        Ensure.False(!condition);
    }

    [Fact]
    public void should_throw_when_condition_is_reverse()
    {
        // given
        bool condition = false;

        // when & then
         Assert.Throws<InvalidOperationException>(() => Ensure.True(condition))
            .Message.Should().Contain($"The condition \"{nameof(condition)}\" must be true");

        var reuslt = Assert.Throws<InvalidOperationException>(() => Ensure.False(!condition))
            .Message.Should().Contain($"The condition \"!{nameof(condition)}\" must be false");

    }
}
