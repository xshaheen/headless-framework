// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Core;

namespace Tests.Core;

public sealed class DisposableFactoryTests
{
    [Fact]
    public void should_invoke_dispose_with_state_when_state_disposable_disposed()
    {
        // given
        var invocations = new List<string>();
        var disposable = DisposableFactory.Create(invocations, static state => state.Add("disposed"));

        // when
        disposable.Dispose();

        // then
        invocations.Should().ContainSingle().Which.Should().Be("disposed");
    }

    [Fact]
    public void should_invoke_dispose_exactly_once_when_state_disposable_disposed_twice()
    {
        // given
        var counter = new Counter();
        var disposable = DisposableFactory.Create(counter, static state => state.Value++);

        // when
        disposable.Dispose();
        disposable.Dispose();

        // then
        counter.Value.Should().Be(1);
    }

    [Fact]
    public async Task should_invoke_dispose_with_state_when_async_state_disposable_disposed()
    {
        // given
        var invocations = new List<string>();

        var disposable = DisposableFactory.Create(
            invocations,
            static state =>
            {
                state.Add("disposed");

                return ValueTask.CompletedTask;
            }
        );

        // when
        await disposable.DisposeAsync();

        // then
        invocations.Should().ContainSingle().Which.Should().Be("disposed");
    }

    [Fact]
    public async Task should_invoke_dispose_exactly_once_when_async_state_disposable_disposed_twice()
    {
        // given
        var counter = new Counter();

        var disposable = DisposableFactory.Create(
            counter,
            static state =>
            {
                state.Value++;

                return ValueTask.CompletedTask;
            }
        );

        // when
        await disposable.DisposeAsync();
        await disposable.DisposeAsync();

        // then
        counter.Value.Should().Be(1);
    }

    [Fact]
    public void should_throw_when_state_dispose_callback_is_null()
    {
        // when
        var action = () => DisposableFactory.Create(state: 42, dispose: (Action<int>)null!);

        // then
        action.Should().Throw<ArgumentNullException>();
    }

    private sealed class Counter
    {
        public int Value { get; set; }
    }
}
