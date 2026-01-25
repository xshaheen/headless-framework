// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reactive.Disposables;
using Microsoft.Extensions.Options;

namespace Tests.Options;

public sealed class OptionsMonitorWrapperTests
{
    [Fact]
    public void should_return_options_from_current_value()
    {
        // given
        var options = new TestOptions { Name = "test-value" };
        var wrapper = new OptionsMonitorWrapper<TestOptions>(options);

        // when
        var result = wrapper.CurrentValue;

        // then
        result.Should().BeSameAs(options);
        result.Name.Should().Be("test-value");
    }

    [Fact]
    public void should_return_same_options_from_get()
    {
        // given
        var options = new TestOptions { Name = "test-value" };
        var wrapper = new OptionsMonitorWrapper<TestOptions>(options);

        // when
        var result = wrapper.Get("some-name");

        // then
        result.Should().BeSameAs(options);
        result.Should().BeSameAs(wrapper.CurrentValue);
    }

    [Fact]
    public void should_return_empty_disposable_from_on_change()
    {
        // given
        var options = new TestOptions { Name = "test-value" };
        var wrapper = new OptionsMonitorWrapper<TestOptions>(options);
        var listenerCalled = false;

        // when
        var result = wrapper.OnChange((_, _) => listenerCalled = true);

        // then
        result.Should().Be(Disposable.Empty);
        listenerCalled.Should().BeFalse();
    }

    private sealed class TestOptions
    {
        public string? Name { get; init; }
    }
}
