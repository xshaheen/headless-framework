// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Abstractions;

namespace Tests.Abstractions;

public sealed class TzConvertTimezoneProviderTests
{
    private readonly TzConvertTimezoneProvider _sut = new();

    [Fact]
    public void should_return_a_non_empty_list_when_get_windows_timezones()
    {
        _sut.GetWindowsTimezones().Should().NotBeEmpty();
    }

    [Fact]
    public void should_return_a_non_empty_list_when_get_iana_timezones()
    {
        _sut.GetIanaTimezones().Should().NotBeEmpty();
    }

    [Fact]
    public void should_return_the_same_cached_instance_across_calls_when_get_windows_timezones()
    {
        // The immutable cache is returned directly — no per-call recomputation or copying.
        _sut.GetWindowsTimezones().Should().BeSameAs(_sut.GetWindowsTimezones());
    }

    [Fact]
    public void should_return_the_same_cached_instance_across_calls_when_get_iana_timezones()
    {
        _sut.GetIanaTimezones().Should().BeSameAs(_sut.GetIanaTimezones());
    }
}
