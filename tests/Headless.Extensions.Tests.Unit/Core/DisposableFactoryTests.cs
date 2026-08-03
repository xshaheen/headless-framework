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

    [Fact]
    public void should_throw_when_async_state_dispose_callback_is_null()
    {
        // when
        var action = () => DisposableFactory.Create(state: 42, dispose: (Func<int, ValueTask>)null!);

        // then
        action.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void should_capture_struct_state_by_value_at_creation()
    {
        // given: the state is copied once at construction, so later mutations of the caller's local are
        // invisible to the dispose callback — pin that so callers don't rely on late binding.
        var state = new MutableStruct { Value = 1 };
        var counter = new Counter();
        var disposable = DisposableFactory.Create(
            (State: state, Sink: counter),
            static scope => scope.Sink.Value = scope.State.Value
        );

        // when
        state.Value = 2;
        disposable.Dispose();

        // then
        counter.Value.Should().Be(1);
    }

    private struct MutableStruct
    {
        public int Value;
    }

    private sealed class Counter
    {
        public int Value { get; set; }
    }
}
