// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;

namespace Tests.Options;

public sealed class OptionsSnapshotWrapperTests
{
    [Fact]
    public void should_return_options_from_value_property()
    {
        // given
        var options = new TestOptions { Name = "test-value" };
        var wrapper = new OptionsSnapshotWrapper<TestOptions>(options);

        // when
        var result = wrapper.Value;

        // then
        result.Should().BeSameAs(options);
        result.Name.Should().Be("test-value");
    }

    [Fact]
    public void should_return_same_options_from_get()
    {
        // given
        var options = new TestOptions { Name = "test-value" };
        var wrapper = new OptionsSnapshotWrapper<TestOptions>(options);

        // when
        var result = wrapper.Get("some-name");

        // then
        result.Should().BeSameAs(options);
        result.Should().BeSameAs(wrapper.Value);
    }

    [Fact]
    public void should_return_options_regardless_of_name()
    {
        // given
        var options = new TestOptions { Name = "test-value" };
        var wrapper = new OptionsSnapshotWrapper<TestOptions>(options);

        // when
        var result1 = wrapper.Get(null);
        var result2 = wrapper.Get("named-options");
        var result3 = wrapper.Get(string.Empty);

        // then
        result1.Should().BeSameAs(options);
        result2.Should().BeSameAs(options);
        result3.Should().BeSameAs(options);
    }

    private sealed class TestOptions
    {
        public string? Name { get; init; }
    }
}
